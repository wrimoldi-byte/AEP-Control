using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

/// <summary>
/// Lee la lista completa de pasajeros filtrados previamente en Sabre por
/// itinerario a EE.UU. y busca el EDIT BKUP en cada fila.
/// El OCR no valida una declaración CDC ni prueba que un EDIT esté ausente.
/// </summary>
public sealed class CdcBkupReader
{
    private static readonly Regex ExactBkup = new(@"\bB\s*K\s*U\s*P\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PossibleBkup = new(@"\b8\s*K\s*U\s*P\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SlashName = new(@"\b(?<last>[A-ZÀ-Ü]{2,})\s*/\s*(?<first>[A-ZÀ-Ü]{2,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberedName = new(
        @"^\s*(?:(?:\d{1,3}|[SIOB])\s*[.)-]?\s+)(?<last>[A-ZÀ-Ü]{3,})\s+(?<first>[A-ZÀ-Ü]{3,})(?:\s+(?<middle>[A-ZÀ-Ü]{3,}))?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Solo la columna Asiento se encuentra tras la clase/tarifa (Y-A 22A, Y-S 30J).
    // Un 1B aislado puede ser una lectura de equipaje, no un asiento.
    private static readonly Regex SeatNearClass = new(@"\b[YWJ]\s*-\s*[A-Z0-9]\s+(?<seat>\d{1,2}[A-HJ-L])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Seat = new(@"\b(?<seat>\d{1,2}[A-HJ-L])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SSR", "EDIT", "EDITS", "PAX", "DOCS", "PASSENGER", "SEAT", "ASIENTO", "BKUP",
        "WCHR", "WCHC", "WCHS", "AVIH", "SABRE", "HK", "NO", "YES", "STATUS",
        "NOMBRE", "APELLIDO", "PASAJERO", "NACIONALIDAD", "DESTINO", "USA"
    };

    private readonly List<CdcPassengerEntry> _passengers = new();
    private readonly List<CdcBkupFinding> _unassigned = new();
    private readonly HashSet<string> _unassignedKeys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CdcPassengerEntry> Passengers => _passengers;
    // Compatibility with the original BKUP-only reader and its regression tests.
    public IReadOnlyList<CdcBkupFinding> Findings => _passengers
        .Where(p => p.Status != CdcBkupStatus.NotDetected)
        .Select(p => new CdcBkupFinding(p.Passenger, p.Evidence, p.Page, p.Status == CdcBkupStatus.Detected))
        .Concat(_unassigned).ToList();
    public int UnidentifiedCount => _unassigned.Count;
    public IReadOnlyList<CdcBkupFinding> Unassigned => _unassigned;
    public int ExactCount => _passengers.Count(p => p.Status == CdcBkupStatus.Detected);
    public int PossibleCount => _passengers.Count(p => p.Status == CdcBkupStatus.Uncertain);
    public int NotDetectedCount => _passengers.Count(p => p.Status == CdcBkupStatus.NotDetected);

    // Variaciones entre pantallas: el OCR puede omitir el segundo nombre.
    // No unir dos pasajeros con segundos nombres diferentes ni asientos incompatibles.
    private static bool SamePassenger(string first, string second)
    {
        if (first == second) return true;
        var a = first.Split('/', 2);
        var b = second.Split('/', 2);
        if (a.Length != 2 || b.Length != 2 || a[0] != b[0]) return false;
        var aNames = a[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bNames = b[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (aNames.Length == 0 || bNames.Length == 0 || aNames[0] != bNames[0]) return false;
        return aNames.Length == 1 || bNames.Length == 1 || a[1] == b[1];
    }

    public void AddPage(string ocrText, int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return;
        var lines = ocrText.ToUpperInvariant().Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw, @"\s+", " ").Trim();
            var exact = ExactBkup.IsMatch(line);
            var uncertain = !exact && PossibleBkup.IsMatch(line);
            var name = SlashName.Match(line);
            var numbered = !name.Success ? NumberedName.Match(line) : Match.Empty;
            var matchedName = name.Success ? name : numbered;
            if (!matchedName.Success || NotNames.Contains(matchedName.Groups["last"].Value) ||
                NotNames.Contains(matchedName.Groups["first"].Value))
            {
                if (exact || uncertain)
                {
                    // Un EDIT no se asigna a la persona de la línea anterior.
                    // No eliminar estos casos: podrían ser pasajeros distintos.
                    var key = pageNumber + ":" + line;
                    if (_unassignedKeys.Add(key))
                        _unassigned.Add(new CdcBkupFinding("", line, pageNumber, exact));
                }
                continue;
            }

            var passenger = matchedName.Groups["last"].Value + "/" + matchedName.Groups["first"].Value;
            // Conservá el segundo nombre visible para distinguir homónimos.
            if (numbered.Success && numbered.Groups["middle"].Success)
                passenger += " " + numbered.Groups["middle"].Value;

            var seatMatch = SeatNearClass.Match(line);
            // En registros con barra (LOPEZ/MARIA 4A), el asiento puede estar
            // inmediatamente después del nombre, sin la clase Y-A.
            if (!seatMatch.Success && name.Success)
                seatMatch = Seat.Match(line[(name.Index + name.Length)..]);
            // Cuando la tabla OCR reconstruida omite la clase y el tratamiento,
            // admitir un asiento inmediatamente después del nombre. Con MR/MS
            // presente, sin contexto de clase, un código como "1B" puede ser
            // equipaje: en ese caso no asignar asiento automáticamente.
            if (!seatMatch.Success && numbered.Success &&
                !Regex.IsMatch(line, @"\b(?:MR|MS|MRS|MSTR)\b"))
                seatMatch = Seat.Match(line[(numbered.Index + numbered.Length)..]);
            var seat = seatMatch.Success ? seatMatch.Groups["seat"].Value : "";
            var status = exact ? CdcBkupStatus.Detected
                : uncertain ? CdcBkupStatus.Uncertain : CdcBkupStatus.NotDetected;

            var existing = _passengers.FirstOrDefault(p =>
                SamePassenger(p.Passenger, passenger) && (p.Seat == seat || p.Seat.Length == 0 || seat.Length == 0));
            if (existing is null)
            {
                _passengers.Add(new CdcPassengerEntry(passenger, seat, pageNumber, line, status));
                continue;
            }

            if (passenger.Length > existing.Passenger.Length) existing.Passenger = passenger;
            if (existing.Seat.Length == 0 && seat.Length != 0) existing.Seat = seat;
            if (status > existing.Status)
            {
                existing.Status = status;
                existing.Evidence = line;
                existing.Page = pageNumber;
            }
        }
    }
}

public enum CdcBkupStatus
{
    NotDetected = 0,
    Uncertain = 1,
    Detected = 2
}

public sealed class CdcPassengerEntry
{
    public string Passenger { get; set; }
    public string Seat { get; set; }
    public int Page { get; set; }
    public string Evidence { get; set; }
    public CdcBkupStatus Status { get; set; }

    public CdcPassengerEntry(string passenger, string seat, int page, string evidence, CdcBkupStatus status)
    {
        Passenger = passenger;
        Seat = seat;
        Page = page;
        Evidence = evidence;
        Status = status;
    }
}

public sealed record CdcBkupFinding(string Passenger, string OcrEvidence, int Page, bool Exact);

public sealed class CdcBkupReviewForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _counter = new();

    public CdcBkupReviewForm(string flight, CdcBkupReader reader, bool incomplete, int pages)
    {
        Text = "CDC EE.UU. — pasajeros filtrados · BKUP" + (flight.Length == 0 ? "" : " · " + flight);
        MinimumSize = new Size(780, 500);
        Size = new Size(1120, 670);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(235, 245, 250);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 122,
            Padding = new Padding(12, 10, 12, 5),
            Font = new Font("Segoe UI", 10),
            Text = $"Lista prefiltrada en Sabre por destino/conexión en EE.UU. · {pages} pantallas.\n" +
                "Se listan todos los nombres reconocidos por OCR, tengan BKUP o no. «No detectado» exige revisión: el OCR puede omitir EDITS y no demuestra ausencia.\n" +
                "BKUP tampoco acredita la declaración del CDC; verificá cada declaración según el procedimiento operativo.\n" +
                (incomplete ? "ATENCIÓN: recorrido PARCIAL; faltan pantallas o se interrumpió la captura." :
                "Comprobá que el número de pasajeros reconocidos coincida con el filtro de Sabre.")
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Passenger", HeaderText = "Pasajero (OCR)", FillWeight = 33, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seat", HeaderText = "Asiento", FillWeight = 12, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "EDIT BKUP", FillWeight = 25, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Page", HeaderText = "Pantalla", FillWeight = 12, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = "Texto OCR para cotejar", FillWeight = 65, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Reviewed", HeaderText = "Declaración CDC revisada", FillWeight = 22 });

        foreach (var entry in reader.Passengers.OrderBy(p => p.Status).ThenBy(p => p.Passenger))
        {
            var state = entry.Status switch
            {
                CdcBkupStatus.Detected => "BKUP detectado",
                CdcBkupStatus.Uncertain => "Posible BKUP — revisar",
                _ => "BKUP no detectado — revisar"
            };
            var row = _grid.Rows[_grid.Rows.Add(entry.Passenger, entry.Seat, state, entry.Page, entry.Evidence, false)];
            if (entry.Status == CdcBkupStatus.NotDetected)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
            else if (entry.Status == CdcBkupStatus.Uncertain)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
        }

        foreach (var unknown in reader.Unassigned)
        {
            var row = _grid.Rows[_grid.Rows.Add(
                "IDENTIDAD NO LEGIBLE — REVISAR", "",
                unknown.Exact ? "BKUP sin identidad" : "Posible BKUP sin identidad",
                unknown.Page, unknown.OcrEvidence, false)];
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
        }

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, _) => UpdateCounter();

        _counter.Dock = DockStyle.Fill;
        _counter.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _counter.Padding = new Padding(10, 10, 0, 0);

        var copy = new Button { Text = "Copiar reporte", AutoSize = true };
        copy.Click += (_, _) =>
        {
            var report = new StringBuilder();
            report.AppendLine("CDC EE.UU. - revisión BKUP - " + flight);
            report.AppendLine("Pantallas: " + pages + (incomplete ? " (INCOMPLETO)" : ""));
            report.AppendLine($"PAX reconocidos: {reader.Passengers.Count}; BKUP detectado: {reader.ExactCount}; posible: {reader.PossibleCount}; no detectado: {reader.NotDetectedCount}; EDIT sin identidad: {reader.Unassigned.Count}");
            report.AppendLine("BKUP es un indicio del EDIT; el OCR no verifica la declaración CDC.");
            foreach (DataGridViewRow row in _grid.Rows)
                report.AppendLine(row.Cells["Passenger"].Value + " | " + row.Cells["Seat"].Value + " | " +
                    row.Cells["State"].Value + " | pantalla " + row.Cells["Page"].Value +
                    " | CDC revisado manualmente: " +
                    (row.Cells["Reviewed"].Value is true ? "SI" : "NO"));
            Clipboard.SetText(report.ToString());
        };
        var close = new Button { Text = "Cerrar", AutoSize = true };
        close.Click += (_, _) => Close();
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 65, ColumnCount = 3, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_counter, 0, 0);
        bottom.Controls.Add(copy, 1, 0);
        bottom.Controls.Add(close, 2, 0);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(intro);
        UpdateCounter();
    }

    private void UpdateCounter()
    {
        var checkedRows = _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Cells["Reviewed"].Value is true);
        _counter.Text = "PAX identificados: " + _grid.Rows.Cast<DataGridViewRow>().Count(r =>
            r.Cells["Passenger"].Value?.ToString() != "IDENTIDAD NO LEGIBLE — REVISAR") +
            " · Declaraciones marcadas: " + checkedRows;
    }
}

/// <summary>
/// Panel flotante: el agente mueve manualmente la lista de Sabre y la
/// aplicación lee cada pantalla distinta sin desplazar nada por su cuenta.
/// </summary>
public sealed class CdcManualScrollForm : Form
{
    private readonly Label _progress = new();
    public bool Finished { get; private set; }
    public bool Cancelled { get; private set; }

    public CdcManualScrollForm(Rectangle area)
    {
        Text = "CDC EE.UU. · lectura manual";
        Size = new Size(400, 230);
        MinimumSize = Size;
        MaximumSize = Size;
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 47, 79);
        ForeColor = Color.White;
        var screen = Screen.FromRectangle(area).WorkingArea;
        var positions = new[]
        {
            new Point(screen.Right - Width, screen.Top),
            new Point(screen.Left, screen.Top),
            new Point(screen.Right - Width, screen.Bottom - Height),
            new Point(screen.Left, screen.Bottom - Height)
        };
        Location = positions.FirstOrDefault(p => !new Rectangle(p, Size).IntersectsWith(area), positions[0]);

        _progress.Dock = DockStyle.Fill;
        _progress.Font = new Font("Segoe UI", 10);
        _progress.Padding = new Padding(14, 12, 10, 5);
        _progress.Text = "Pasajeros OCR: 0\nBKUP: 0 · Sin detectar: 0\n\nDesplazá lentamente Sabre, con filas repetidas.\nPausá 1–2 segundos por pantalla.";

        var end = new Button { Text = "Terminar y ver lista", AutoSize = true, Padding = new Padding(6) };
        end.Click += (_, _) => Finished = true;
        var cancel = new Button { Text = "Cancelar", AutoSize = true, Padding = new Padding(6) };
        cancel.Click += (_, _) => Cancelled = true;
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(7) };
        controls.Controls.AddRange(new Control[] { end, cancel });
        Controls.Add(_progress);
        Controls.Add(controls);
        FormClosing += (_, _) => { if (!Finished) Cancelled = true; };
    }

    public void UpdateProgress(int pages, CdcBkupReader reader)
    {
        _progress.Text = $"Pantallas distintas: {pages}\n" +
            $"PAX identificados: {reader.Passengers.Count}\n" +
            $"BKUP: {reader.ExactCount} · Dudosos: {reader.PossibleCount}\n" +
            $"BKUP no detectado: {reader.NotDetectedCount}\n" +
            "Seguí haciendo scroll lento. Al terminar pulsá «Terminar y ver lista».";
    }
}

public sealed partial class BubbleMainForm
{
    private bool _cdcScanBusy;

    private async Task ScanCdcBkupAsync()
    {
        if (_cdcScanBusy || _visionBusy) return;
        if (!FinishTableEditing()) return;
        var flight = GetSelectedFlight()?.Vuelo ?? "Lista EE.UU.";
        if (MessageBox.Show(this,
            "Primero filtrá en Sabre todos los pasajeros cuyo itinerario termine o conecte en EE.UU.\n" +
            "Abrí la lista de pasajeros con NOMBRES y EDITS, volvé al principio y seleccioná toda la zona de las filas.\n\n" +
            "VOS hacés scroll manualmente. AEP Control leerá mediante OCR local las pantallas que cambien, " +
            "identificará cada pasajero y buscará BKUP. Pausá 1–2 segundos por pantalla y repetí 2 filas " +
            "entre movimientos.\n\nBKUP es un indicio, no una confirmación de CDC. ¿Comenzar?",
            "CDC EE.UU. · lista filtrada", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        _cdcScanBusy = true;
        using var cts = new CancellationTokenSource();
        var reader = new CdcBkupReader();
        var screensRead = 0;
        Bitmap? lastScreen = null;
        try
        {
            Hide();
            await Task.Delay(250, cts.Token);
            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK)
            {
                _status.Text = "CDC: selección cancelada.";
                return;
            }

            var area = selector.SelectedArea;
            using var bubble = new CdcManualScrollForm(area);
            bubble.Show();
            await Task.Delay(300, cts.Token);

            while (!bubble.Finished && !bubble.Cancelled)
            {
                // Evitar procesar pantallas idénticas, pero retener solapamientos
                // para que un pasajero leído en dos pantallas no se duplique.
                using var image = CaptureArea(area);
                if (lastScreen is null || !FramesAreSimilar(lastScreen, image))
                {
                    var text = await OcrService.ReadCdcRowsAsync(image);
                    reader.AddPage(text, ++screensRead);
                    lastScreen?.Dispose();
                    lastScreen = (Bitmap)image.Clone();
                    if (!bubble.IsDisposed) bubble.UpdateProgress(screensRead, reader);
                }
                if (!bubble.Finished && !bubble.Cancelled)
                    await Task.Delay(500, cts.Token);
            }

            if (bubble.Cancelled)
            {
                _status.Text = "CDC: lectura cancelada.";
                return;
            }
            bubble.Close();
            Show();
            Activate();
            var finishedAtBottom = MessageBox.Show(this,
                "¿Llegaste al FINAL de la lista filtrada de Sabre?\n" +
                "Si todavía quedan pasajeros sin recorrer, indicaremos que la revisión es parcial.",
                "CDC EE.UU. · confirmar cobertura", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            if (reader.Passengers.Count == 0 && reader.Unassigned.Count == 0)
                MessageBox.Show(this, "No pude reconocer nombres ni BKUP. Revisá el recorte: deben verse los nombres de los pasajeros junto a los EDITS.",
                    "CDC EE.UU.", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            _status.Text = $"CDC: {reader.Passengers.Count} PAX OCR · BKUP {reader.ExactCount} · " +
                $"sin detectar {reader.NotDetectedCount} · dudosos {reader.PossibleCount}." +
                (!finishedAtBottom ? " LECTURA PARCIAL." : " Verificá el total contra Sabre.");
            using var review = new CdcBkupReviewForm(flight, reader, !finishedAtBottom, screensRead);
            ShowForegroundDialog(review);
        }
        catch (OperationCanceledException) { _status.Text = "CDC: lectura cancelada."; }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CDC BKUP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = "CDC: lectura interrumpida.";
        }
        finally
        {
            lastScreen?.Dispose();
            _cdcScanBusy = false;
            if (!IsDisposed) { Show(); Activate(); }
        }
    }
}
