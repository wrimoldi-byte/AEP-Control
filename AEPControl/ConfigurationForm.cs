namespace AEPControl;

public sealed class ConfigurationForm : Form
{
    private readonly TextBox _codes = new();

    public ConfigurationForm()
    {
        Text = "Configuración — Edits especiales";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(520, 590);
        MinimizeBox = false;
        MaximizeBox = false;

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Text = "Códigos que debe reconocer el OCR",
            Padding = new Padding(12, 10, 0, 0)
        };

        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            Text = "Poné un código por línea. Podés agregar, quitar o corregir edits sin recompilar la app. Ejemplo: WCLB, UMNR, INAD.",
            Padding = new Padding(12, 6, 12, 0)
        };

        _codes.Dock = DockStyle.Fill;
        _codes.Multiline = true;
        _codes.ScrollBars = ScrollBars.Vertical;
        _codes.Font = new Font("Consolas", 11);
        _codes.AcceptsReturn = true;
        _codes.WordWrap = false;
        _codes.Text = string.Join(Environment.NewLine, SpecialCodeSettings.Load().Codes);

        var save = new Button { Text = "Guardar", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
        save.Click += (_, _) => SaveAndClose();

        var restore = new Button { Text = "Restaurar predeterminados", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        restore.Click += (_, _) => _codes.Text = string.Join(Environment.NewLine, SpecialCodeSettings.DefaultCodes);

        var cancel = new Button { Text = "Cancelar", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        cancel.Click += (_, _) => Close();

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(10, 8, 10, 4),
            FlowDirection = FlowDirection.RightToLeft
        };
        bar.Controls.Add(cancel);
        bar.Controls.Add(save);
        bar.Controls.Add(restore);

        Controls.Add(_codes);
        Controls.Add(bar);
        Controls.Add(help);
        Controls.Add(title);
    }

    private void SaveAndClose()
    {
        var raw = _codes.Text
            .Replace(',', '\n')
            .Replace(';', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var normalized = SpecialCodeSettings.Normalize(raw);
        if (normalized.Count == 0)
        {
            MessageBox.Show("Tiene que quedar al menos un código válido.", "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        new SpecialCodeSettings { Codes = normalized }.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
