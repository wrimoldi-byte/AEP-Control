namespace AEPControl;

public enum BubbleMode
{
    SpecialPassengers,
    FlightList,
    PassengerDocuments
}

public sealed class BubbleForm : Form
{
    private readonly Label _flight = new();
    private readonly Label _counts = new();
    private readonly Label _state = new();
    private readonly Button _finish = new();
    private Point _dragStart;

    public event EventHandler? FinishRequested;

    public BubbleForm(string flight, BubbleMode mode = BubbleMode.SpecialPassengers)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Size = new Size(270, 132);
        BackColor = Color.FromArgb(35, 35, 42);
        ForeColor = Color.White;
        Opacity = 0.94;
        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 28, 28));

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(screen.Right - Width - 18, screen.Top + 18);

        _flight.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        _flight.Location = new Point(18, 10);
        _flight.Size = new Size(155, 28);
        _flight.Text = flight;

        _state.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _state.Location = new Point(175, 15);
        _state.Size = new Size(78, 20);
        _state.Text = "● Leyendo";
        _state.ForeColor = Color.LightGreen;

        _counts.Font = new Font("Segoe UI", 9);
        _counts.Location = new Point(18, 42);
        _counts.Size = new Size(234, 50);
        _counts.Text = mode switch
        {
            BubbleMode.FlightList => "Vuelos únicos: 0\nHacé scroll lentamente",
            BubbleMode.PassengerDocuments => "Documentos únicos: 0\nOCR continuo · pasada 0",
            _ => "WCHR 0 · WCHS 0 · WCHC 0\nAVIH 0 · INF 0\nOtros 0 · filas 0"
        };

        _finish.Text = mode switch
        {
            BubbleMode.FlightList => "Finalizar lista",
            BubbleMode.PassengerDocuments => "Finalizar documentación",
            _ => "Finalizar y seguir"
        };
        _finish.Location = new Point(18, 96);
        _finish.Size = new Size(234, 27);
        _finish.Click += (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { _flight, _state, _counts, _finish });

        MouseDown += StartDrag;
        MouseMove += Drag;
        foreach (Control c in Controls)
        {
            if (c == _finish) continue;
            c.MouseDown += StartDrag;
            c.MouseMove += Drag;
        }
    }

    public void UpdateCounts(FlightData flight, int uniqueRows)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateCounts(flight, uniqueRows));
            return;
        }

        _flight.Text = flight.Vuelo;
        _counts.Text = $"WCHR {flight.WCHR} · WCHS {flight.WCHS} · WCHC {flight.WCHC}\nAVIH {flight.AVIH} · INF {flight.INF}\nOtros {flight.OtrosEspecialesTotal} · filas {uniqueRows}";
    }

    public void UpdateFlightCount(int count)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateFlightCount(count));
            return;
        }

        _counts.Text = $"Vuelos únicos: {count}\nHacé scroll lentamente";
    }

    public void UpdateDocumentCount(int count, int pass = 0)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateDocumentCount(count, pass));
            return;
        }

        _state.Text = pass % 2 == 0 ? "● Leyendo" : "◉ Leyendo";
        _counts.Text = $"Documentos únicos: {count}\nOCR continuo · pasada {pass}";
    }

    public void UpdatePassengerDocumentCount(int count, int pass = 0) => UpdateDocumentCount(count, pass);

    public void SetStopped()
    {
        _state.Text = "● Detenido";
        _state.ForeColor = Color.Khaki;
    }

    private void StartDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _dragStart = e.Location;
    }

    private void Drag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var source = sender as Control;
        var screenPoint = source?.PointToScreen(e.Location) ?? PointToScreen(e.Location);
        Location = new Point(screenPoint.X - _dragStart.X, screenPoint.Y - _dragStart.Y);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
}
