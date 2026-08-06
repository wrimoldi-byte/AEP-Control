namespace AEPControl;

public sealed class SelectionForm : Form
{
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    public Rectangle SelectedArea { get; private set; }

    public SelectionForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0.25;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        ShowInTaskbar = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _start = e.Location;
        _dragging = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _selection = Rectangle.FromLTRB(
            Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
            Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_selection.Width < 20 || _selection.Height < 20) return;
        SelectedArea = RectangleToScreen(_selection);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_selection.IsEmpty) return;
        using var pen = new Pen(Color.DeepSkyBlue, 3);
        e.Graphics.DrawRectangle(pen, _selection);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
