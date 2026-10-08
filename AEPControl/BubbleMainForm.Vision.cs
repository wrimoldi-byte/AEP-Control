using System.ComponentModel;

namespace AEPControl;

public sealed partial class BubbleMainForm
{
    private readonly ComboBox _readerMode = new();
    private readonly Dictionary<FlightData, VisionSpecialAccumulator> _visionSpecials = new();
    private CancellationTokenSource? _visionCts;
    private bool _visionBusy;
    private bool UseVision => _readerMode.SelectedIndex == 1;

    private void ConfigureVisionAndEditing(FlowLayoutPanel bar)
    {
        _readerMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _readerMode.Width = 155;
        _readerMode.Items.AddRange(new object[] { "OCR local (Windows)", "IA Gemini (captura)" });
        _readerMode.SelectedIndex = 0;
        _readerMode.SelectedIndexChanged += (_, _) =>
        {
            _status.Text = UseVision ? "IA: una captura por clic. Volvé a capturar cada página al hacer scroll." : "OCR local: lectura continua disponible.";
            _help.Text = UseVision
                ? "IA Gemini: capturá una pantalla quieta por vez. Revisá y aceptá los datos. Para EDITS, repetí por cada página del mismo vuelo; las filas identificadas se acumulan sin sumar dos veces. Doble clic en una celda para editar sin ventanas."
                : "OCR local: lectura continua mientras hacés scroll. Doble clic en una celda para editar; Enter/Tab guarda, Esc cancela; botón Leer EDITS para escanear.";
        };
        var configure = new Button { Text = "Configurar IA", AutoSize = true };
        configure.Click += (_, _) => { using var dialog = new VisionSettingsForm(); ShowForegroundDialog(dialog); };
        var edit = new Button { Text = "Editar celda (F2)", AutoSize = true };
        edit.Click += (_, _) => EditSelectedFlight();
        var reread = new Button { Text = "Permitir releer", AutoSize = true };
        reread.Click += (_, _) =>
        {
            var selected = GetSelectedFlight();
            if (selected is null) return;
            if (MessageBox.Show(this, $"¿Desproteger las correcciones de {selected.Vuelo} y comenzar una nueva lectura de EDITS? Los valores actuales se conservan hasta aceptar datos nuevos.",
                    "Permitir releer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            selected.ManualFields.Clear();
            _visionSpecials.Remove(selected);
            _arrivalGrid.Refresh(); _departureGrid.Refresh();
            _status.Text = $"{selected.Vuelo}: habilitado para una nueva lectura.";
        };
        bar.Controls.AddRange(new Control[] { _readerMode, configure, edit, reread });
        bar.Controls.SetChildIndex(_readerMode, 0);
        bar.Height = 135;
        foreach (var button in new[] { configure, edit, reread })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(137, 37, 103);
            button.ForeColor = Color.White;
            button.Padding = new Padding(8, 7, 8, 7);
        }
    }

    private DialogResult ShowForegroundDialog(Form dialog)
    {
        var wasTopMost = TopMost;
        try
        {
            TopMost = false;
            dialog.TopMost = true;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Shown += (_, _) => { dialog.BringToFront(); dialog.Activate(); };
            return dialog.ShowDialog(this);
        }
        finally
        {
            TopMost = wasTopMost;
            if (!IsDisposed) { BringToFront(); Activate(); }
        }
    }

    private void EditSelectedFlight()
    {
        if (_visionBusy) return;
        var selected = GetSelectedFlight();
        if (selected is null) { _status.Text = "Seleccioná una celda para editar."; return; }
        var grid = _arrivals.Contains(selected) ? _arrivalGrid : _departureGrid;
        if (grid.CurrentCell is null || grid.CurrentCell.ReadOnly)
            grid.CurrentCell = grid.CurrentRow!.Cells[nameof(FlightData.Edits)];
        grid.Focus();
        grid.BeginEdit(true);
    }

    private static void MergeRecognizedFlight(BindingList<FlightData> target, FlightData incoming)
    {
        var identity = VisionResult.FlightNumber(incoming.Vuelo);
        var existing = target.FirstOrDefault(f => VisionResult.FlightNumber(f.Vuelo) == identity ||
            (f.SourceFlight.Length > 0 && VisionResult.FlightNumber(f.SourceFlight) == identity));
        if (existing is null) target.Add(incoming);
        else FlightCorrections.Merge(existing, incoming);
        target.ResetBindings();
    }

    private async Task CaptureWithVisionAsync(string kind, string movement)
    {
        if (!FinishTableEditing()) return;
        if (_visionBusy) return;
        var selected = kind == "ito" ? _departureGrid.CurrentRow?.DataBoundItem as FlightData : GetSelectedFlight();
        if (kind is "ito" or "specials" && selected is null)
        {
            MessageBox.Show(this, "Seleccioná el vuelo correspondiente antes de capturar.", "Captura IA");
            return;
        }
        if (kind == "specials" && selected!.ManualFields.Contains(nameof(FlightData.Edits)))
        {
            MessageBox.Show(this, "Los EDITS tienen correcciones manuales protegidas. Usá «Permitir releer» para reemplazarlos.", "Captura IA");
            return;
        }
        var settings = VisionSettings.Load();
        if (settings.ProtectedKey.Length == 0 || !settings.FreeProjectConfirmed)
        {
            using var setup = new VisionSettingsForm();
            if (ShowForegroundDialog(setup) != DialogResult.OK) return;
            settings = VisionSettings.Load();
            if (settings.ProtectedKey.Length == 0 || !settings.FreeProjectConfirmed) return;
        }
        _visionBusy = true;
        _visionCts = new CancellationTokenSource();
        var token = _visionCts.Token;
        try
        {
            Hide();
            await Task.Delay(250, token);
            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK) return;
            using var bitmap = new Bitmap(selector.SelectedArea.Width, selector.SelectedArea.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(selector.SelectedArea.Location, Point.Empty, selector.SelectedArea.Size);
            Show(); Activate();
            using (var preview = new VisionCaptureDialog(bitmap))
                if (ShowForegroundDialog(preview) != DialogResult.OK) return;
            settings.ReserveRequest();
            Enabled = false;
            _status.Text = $"Consultando Gemini ({settings.RequestsToday}/{settings.DailyLimit})…";
            var result = await new GeminiVisionClient().ReadAsync(bitmap, kind, movement, selected?.Vuelo ?? "", settings, token);
            token.ThrowIfCancellationRequested();
            Enabled = true;
            if (kind is "ito" or "specials" && result.Ito.Vuelo.Length > 0 &&
                VisionResult.FlightNumber(result.Ito.Vuelo) != VisionResult.FlightNumber(selected!.Vuelo) &&
                VisionResult.FlightNumber(result.Ito.Vuelo) != VisionResult.FlightNumber(selected.SourceFlight))
                throw new InvalidOperationException($"La captura corresponde a {result.Ito.Vuelo}, pero seleccionaste {selected.Vuelo}. No se cargaron los datos.");
            using (var review = new VisionCaptureDialog(bitmap, Summary(result, kind, selected?.Vuelo ?? movement)))
                if (ShowForegroundDialog(review) != DialogResult.OK) { _status.Text = "Respuesta descartada. Los datos anteriores se conservan."; return; }
            if (kind == "flights")
            {
                if (result.Flights.Count == 0) throw new InvalidOperationException("No se reconocieron vuelos. Los anteriores se conservan.");
                var target = movement == "Llegada" ? _arrivals : _departures;
                foreach (var flight in result.Flights)
                {
                    flight.Movimiento = movement;
                    MergeRecognizedFlight(target, flight);
                }
                _action.Visible = true;
                _action.Text = "Leer EDITS del vuelo seleccionado";
                _readDepartureOperation.Enabled = _departures.Count > 0;
                _status.Text = $"{result.Flights.Count} vuelos leídos. Podés capturar la próxima página o editar con doble clic.";
            }
            else if (kind == "ito")
            {
                if (!result.Ito.HasOperationalData) throw new InvalidOperationException("No se reconocieron datos del ITO. Los anteriores se conservan.");
                FlightCorrections.ApplyText(selected!, nameof(FlightData.Matricula), result.Ito.Matricula);
                FlightCorrections.ApplyText(selected!, nameof(FlightData.Configuracion), result.Ito.Configuracion);
                FlightCorrections.ApplyText(selected!, nameof(FlightData.Servicios), result.Ito.Servicios);
                _status.Text = $"{selected!.Vuelo}: ITO cargado. Doble clic para corregir los datos.";
            }
            else
            {
                if (result.Specials.Count == 0) throw new InvalidOperationException("No se reconocieron filas identificables de EDITS. Los valores anteriores se conservan; completalos manualmente si corresponde.");
                if (!_visionSpecials.TryGetValue(selected!, out var accumulator))
                    _visionSpecials[selected!] = accumulator = new VisionSpecialAccumulator();
                accumulator.Add(result.Specials);
                FlightCorrections.SetCounts(selected!, accumulator.Counts());
                _status.Text = $"{selected!.Vuelo}: {accumulator.UniquePassengers} pasajeros identificados. Capturá la siguiente página o editá los totales.";
            }
            _arrivals.ResetBindings(); _departures.ResetBindings();
            _export.Enabled = AllFlights().Any();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Lectura IA cancelada o tiempo de espera agotado. Los datos anteriores se conservan."; }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, ex is System.Net.Http.HttpRequestException ? "No se pudo conectar con Gemini. Revisá internet. Los datos anteriores se conservan." :
                ex is System.Text.Json.JsonException or KeyNotFoundException ? "Gemini devolvió una respuesta inválida. No se cargaron datos." : ex.Message,
                "Lectura IA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _visionBusy = false;
            _visionCts?.Dispose(); _visionCts = null;
            if (!IsDisposed) { Enabled = true; Show(); Activate(); }
        }
    }

    private static string Summary(VisionResult result, string kind, string context)
    {
        var text = $"Destino de la lectura: {context}\r\n\r\n";
        text += kind switch
        {
            "flights" => string.Join("\r\n", result.Flights.Select(f => $"{f.Vuelo} · {f.Destino} · {f.Hora} · PAX {f.Booking} · {f.Equipo}")),
            "ito" => $"Matrícula: {result.Ito.Matricula}\r\nConfiguración: {result.Ito.Configuracion}\r\nServicios: {result.Ito.Servicios}",
            _ => string.Join("\r\n", result.Specials.Select(r => $"{r.Identity}: {string.Join(", ", r.Codes)}"))
        };
        if (result.Warnings.Count > 0) text += "\r\n\r\nREVISAR:\r\n" + string.Join("\r\n", result.Warnings);
        return text + "\r\n\r\nDespués de cargar, editá directamente en la tabla con doble clic o F2. Enter/Tab guarda; Esc cancela.";
    }
}

internal sealed class VisionCaptureDialog : Form
{
    public VisionCaptureDialog(Bitmap bitmap, string? summary = null)
    {
        Text = summary is null ? "Revisar captura antes de enviar" : "Revisar respuesta de Gemini";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(950, 700);
        MinimumSize = new Size(750, 500);
        var image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = bitmap };
        var note = new TextBox { Dock = DockStyle.Bottom, Height = summary is null ? 75 : 200, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = summary ?? "Esta captura se enviará a Google Gemini. Usá solo capturas ficticias o anonimizadas, sin información personal ni confidencial. El servicio gratuito puede usar el contenido para mejorar sus productos." };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var accept = new Button { Text = summary is null ? "Enviar captura" : "Cargar datos", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { accept, cancel });
        Controls.Add(image); Controls.Add(note); Controls.Add(buttons);
        AcceptButton = accept; CancelButton = cancel;
    }
}
