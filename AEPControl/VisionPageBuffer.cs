using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace AEPControl;

public sealed class VisionPageBuffer : IDisposable
{
    public const int MaxPages = 12;
    public const long MaxBytes = 12 * 1024 * 1024;
    private readonly List<Bitmap> _pages = new();
    private readonly HashSet<string> _seen = new();
    private string _candidate = "";
    private int _stableSamples;
    private long _bytes;
    private long _pixels;
    public IReadOnlyList<Bitmap> Pages => _pages;
    public bool LimitReached { get; private set; }

    public bool Observe(Bitmap frame, bool captureNow = false)
    {
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFormat.Png);
        var hash = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        _stableSamples = hash == _candidate ? _stableSamples + 1 : 1;
        _candidate = hash;
        if ((!captureNow && _stableSamples < 3) || _seen.Contains(hash)) return false;
        if (_pages.Count >= MaxPages || _bytes + stream.Length > MaxBytes || _pixels + (long)frame.Width * frame.Height > 24_000_000)
        {
            LimitReached = true;
            return false;
        }
        _pages.Add((Bitmap)frame.Clone());
        _seen.Add(hash);
        _bytes += stream.Length;
        _pixels += (long)frame.Width * frame.Height;
        return true;
    }

    public void Dispose()
    {
        foreach (var page in _pages) page.Dispose();
        _pages.Clear();
    }
}

public sealed class VisionScrollCaptureForm : Form
{
    private readonly Button _finish = new() { Text = "Terminar y revisar", AutoSize = true };
    private readonly Label _state = new() { Dock = DockStyle.Fill, Padding = new Padding(10), ForeColor = Color.White };
    public bool Finished { get; private set; }
    public bool Cancelled { get; private set; }
    public bool CaptureRequested { get; set; }

    public VisionScrollCaptureForm(string flight, Rectangle captureArea)
    {
        Text = $"EDITS IA · {flight}";
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(340, 160);
        BackColor = Color.FromArgb(18, 42, 70);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(5), BackColor = Color.White };
        var capture = new Button { Text = "Capturar ahora", AutoSize = true };
        var cancel = new Button { Text = "Cancelar", AutoSize = true };
        capture.Click += (_, _) => CaptureRequested = true;
        _finish.Click += (_, _) => Finished = true;
        cancel.Click += (_, _) => Cancelled = true;
        buttons.Controls.AddRange(new Control[] { capture, _finish, cancel });
        Controls.Add(_state); Controls.Add(buttons);
        FormClosing += (_, _) => { if (!Finished) Cancelled = true; };
        var area = Screen.FromRectangle(captureArea).WorkingArea;
        var positions = new[] { new Point(area.Right - Width, area.Top), new Point(area.Left, area.Top),
            new Point(area.Right - Width, area.Bottom - Height), new Point(area.Left, area.Bottom - Height) };
        Location = positions.FirstOrDefault(p => !new Rectangle(p, Size).IntersectsWith(captureArea), positions[0]);
    }

    public void UpdateState(int count, bool limit, bool overlaps)
    {
        _finish.Enabled = !overlaps;
        _state.Text = overlaps ? "Mové esta ventana fuera del recorte para seguir capturando. Podés arrastrarla desde el título."
            : limit ? $"{count} pantallas guardadas. Lote lleno: terminá y revisá. Después podés continuar desde la pantalla pendiente en el mismo vuelo."
            : $"{count} pantallas guardadas · captura local\nHacé scroll en Sabre y pausá 1 segundo por pantalla. Dejá 2 filas repetidas entre páginas.\nLa IA procesa al terminar, no durante el scroll.";
    }
}
