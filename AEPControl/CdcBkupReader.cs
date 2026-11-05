using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

/// <summary>
/// Lee solamente indicios de EDIT BKUP desde el texto OCR de una lista de Sabre.
/// No verifica firmas ni certificados del CDC. Sin una lista completa de viajeros a
/// EE.UU. no es posible determinar quien carece de declaración.
/// </summary>
public sealed class CdcBkupReader
{
    private static readonly Regex ExactBkup = new(@"\bB\s*K\s*U\s*P\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PossibleBkup = new(@"\b8\s*K\s*U\s*P\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Name = new(@"\b(?<last>[A-Z]{2,})\s*/\s*(?<first>[A-Z]{2,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly List<CdcBkupFinding> _findings = new();
    private readonly HashSet<string> _seenNamed = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CdcBkupFinding> Findings => _findings;
    public int ExactCount => _findings.Count(x => x.Exact);
    public int PossibleCount => _findings.Count(x => !x.Exact);
    public int UnidentifiedCount => _findings.Count(x => x.Passenger.Length == 0);

    public void AddPage(string ocrText, int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return;
        var lines = ocrText.ToUpperInvariant().Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = Regex.Replace(lines[lineIndex], @"\s+", " ").Trim();
            var exact = ExactBkup.IsMatch(line);
            if (!exact && !PossibleBkup.IsMatch(line)) continue;

            // Usar el nombre solo cuando OCR lo encontró en LA MISMA FILA.
            // Si está en otra fila, no adivinar a qué pasajero pertenece el edit.
            var match = Name.Match(line);
            var passenger = match.Success
                ? match.Groups["last"].Value + "/" + match.Groups["first"].Value
                : "";
            if (passenger.Length > 0 && !_seenNamed.Add(passenger))
            {
                // Si una pasada posterior sí leyó "BKUP" inequívocamente,
                // actualizar una detección OCR dudosa previa del mismo pasajero.
                if (exact)
                {
                    var index = _findings.FindIndex(f => f.Passenger == passenger && !f.Exact);
                    if (index >= 0)
                        _findings[index] = new CdcBkupFinding(passenger, line, pageNumber, true);
                }
                continue;
            }

            // Sin identidad NO deduplicamos por texto BKUP: podrían ser varias
            // personas con un OCR igual. Se muestran para revisión, sin afirmar
            // que el número de coincidencias sea un total de pasajeros únicos.
            _findings.Add(new CdcBkupFinding(passenger, line, pageNumber, exact));
        }
    }
}

public sealed record CdcBkupFinding(string Passenger, string OcrEvidence, int Page, bool Exact);

public sealed class CdcBkupReviewForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _counter = new();

    public CdcBkupReviewForm(string flight, CdcBkupReader reader, bool incomplete, int pages)
    {
        Text = "CDC EE.UU. — revisión de BKUP" + (flight.Length == 0 ? "" : " · " + flight);
        MinimumSize = new Size(750, 480);
        Size = new Size(1000, 610);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(235, 245, 250);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 104,
            Padding = new Padding(12, 11, 12, 5),
            Font = new Font("Segoe UI", 10),
            Text = "Se buscaron EDITS BKUP en " + pages + " pantallas de Sabre (" + flight + ").\n" +
                "BKUP es un indicio operativo, NO una aprobación del CDC. Confirmá por separado la declaración firmada de cada pasajero con destino o conexión en EE.UU.\n" +
                "Los pasajeros sin BKUP no pueden identificarse usando únicamente la lista de EDITS." +
                (incomplete ? "\nATENCIÓN: lote parcial. Continuá desde donde quedó Sabre para cubrir el resto." : "")
        };
        if (incomplete) intro.Height = 127;

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Passenger", HeaderText = "Pasajero (OCR)", FillWeight = 30, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "EDIT", FillWeight = 22, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Page", HeaderText = "Pantalla", FillWeight = 12, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = "Texto OCR para revisar", FillWeight = 70, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Reviewed", HeaderText = "Declaración CDC revisada", FillWeight = 25 });

        foreach (var finding in reader.Findings)
        {
            var row = _grid.Rows[_grid.Rows.Add(
                finding.Passenger.Length == 0 ? "Sin identidad — revisar" : finding.Passenger,
                finding.Exact ? "BKUP detectado" : "Posible BKUP (8KUP)",
                finding.Page,
                finding.OcrEvidence,
                false)];
            if (!finding.Exact || finding.Passenger.Length == 0)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
        }

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, _) => UpdateCounter();

        _counter.Dock = DockStyle.Fill;
        _counter.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _counter.Padding = new Padding(12, 10, 0, 0);

        var copy = new Button { Text = "Copiar reporte", AutoSize = true };
        copy.Click += (_, _) =>
        {
            var report = new StringBuilder();
            report.AppendLine("CDC EE.UU. - BKUP - " + flight);
            report.AppendLine("Pantallas capturadas: " + pages + (incomplete ? " (LOTE PARCIAL)" : ""));
            report.AppendLine("BKUP es indicio de edit. No verifica declaración firmada.");
            foreach (DataGridViewRow row in _grid.Rows)
                report.AppendLine(row.Cells["Passenger"].Value + " | " + row.Cells["State"].Value +
                    " | pantalla " + row.Cells["Page"].Value + " | CDC revisado manualmente: " +
                    (row.Cells["Reviewed"].Value is true ? "SI" : "NO") + " | " + row.Cells["Evidence"].Value);
            Clipboard.SetText(report.ToString());
        };
        var close = new Button { Text = "Cerrar", AutoSize = true };
        close.Click += (_, _) => Close();
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 56, ColumnCount = 3, Padding = new Padding(8) };
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
        _counter.Text = "Coincidencias OCR: " + _grid.Rows.Count + " · revisadas manualmente: " + checkedRows +
            " (no equivale a total de PAX)";
    }
}

public sealed partial class BubbleMainForm
{
    private bool _cdcScanBusy;

    private async Task ScanCdcBkupAsync()
    {
        if (_cdcScanBusy || _visionBusy) return;
        if (!FinishTableEditing()) return;

        var flight = GetSelectedFlight()?.Vuelo ?? "Vuelo no seleccionado";
        if (MessageBox.Show(this,
            "Abrí en Sabre la lista de EDITS de los pasajeros que viajan o conectan a Estados Unidos.\n" +
            "Colocá la lista AL PRINCIPIO. Vas a seleccionar el área con nombres y EDITS; " +
            "AEP Control hará scroll y buscará BKUP mediante OCR local.\n\n" +
            "La captura no confirma la declaración del CDC y puede requerir varios lotes de hasta 12 pantallas.\n\n¿Comenzar?",
            "Revisión CDC EE.UU. · BKUP", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        _cdcScanBusy = true;
        using var tokenSource = new CancellationTokenSource();
        using var pages = new VisionPageBuffer();
        var token = tokenSource.Token;
        try
        {
            Hide();
            await Task.Delay(250, token);
            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK)
            {
                _status.Text = "Búsqueda CDC BKUP cancelada.";
                return;
            }

            using (var first = await CaptureStableAreaAsync(selector.SelectedArea, token))
                pages.Observe(first, captureNow: true);
            if (pages.Pages.Count == 0)
                throw new InvalidOperationException("El área seleccionada es demasiado grande. Recortá solo la lista de EDITS.");
            if (!await CaptureEditsAutoScrollAsync(selector.SelectedArea, pages, flight, token))
            {
                _status.Text = "Búsqueda CDC BKUP cancelada con Esc.";
                return;
            }

            Show();
            Activate();
            _status.Text = "Leyendo BKUP por OCR local en " + pages.Pages.Count + " capturas…";
            var reader = new CdcBkupReader();
            for (var i = 0; i < pages.Pages.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                _status.Text = "CDC BKUP: analizando pantalla " + (i + 1) + "/" + pages.Pages.Count + "…";
                var text = await OcrService.ReadContinuousAsync(pages.Pages[i]);
                reader.AddPage(text, i + 1);
            }
            _status.Text = "CDC BKUP: " + reader.ExactCount + " coincidencias exactas, " +
                reader.PossibleCount + " dudosas." + (pages.LimitReached ? " Lectura parcial." : "");
            using var review = new CdcBkupReviewForm(flight, reader, pages.LimitReached, pages.Pages.Count);
            ShowForegroundDialog(review);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Búsqueda CDC BKUP cancelada; no se modificaron datos.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "CDC BKUP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = "Búsqueda CDC BKUP no completada.";
        }
        finally
        {
            _cdcScanBusy = false;
            if (!IsDisposed) { Show(); Activate(); }
        }
    }
}
