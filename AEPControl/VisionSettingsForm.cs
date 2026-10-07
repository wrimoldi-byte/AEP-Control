using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class VisionSettingsForm : Form
{
    public VisionSettingsForm()
    {
        var settings = VisionSettings.Load();
        Text = "Configurar IA Gemini";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(600, 470);
        MinimumSize = Size;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = "Clave de API (se protege con tu usuario de Windows)", AutoSize = true });
        var key = new TextBox { Width = 540, UseSystemPasswordChar = true, PlaceholderText = settings.ProtectedKey.Length > 0 ? "Clave guardada. Dejá vacío para conservarla." : "Pegá aquí la clave de Google AI Studio" };
        panel.Controls.Add(key);
        panel.Controls.Add(new Label { Text = "Modelo con visión (sin cambio automático a otro modelo)", AutoSize = true });
        var model = new TextBox { Width = 540, Text = settings.Model };
        panel.Controls.Add(model);
        panel.Controls.Add(new Label { Text = "Máximo de consultas por día en esta PC", AutoSize = true });
        var limit = new NumericUpDown { Minimum = 1, Maximum = 500, Value = Math.Clamp(settings.DailyLimit, 1, 500), Width = 100 };
        panel.Controls.Add(limit);
        var confirm = new CheckBox { Text = "Confirmo que mi proyecto de Google tiene la facturación desactivada.", Checked = settings.FreeProjectConfirmed, AutoSize = true };
        panel.Controls.Add(confirm);
        panel.Controls.Add(new Label { Text = "El programa no puede verificar la facturación de Google ni garantizar una cuota gratuita. No activa pagos, cambia modelos ni reintenta automáticamente. La API requiere internet.", AutoSize = true, MaximumSize = new Size(540, 0) });
        panel.Controls.Add(new Label { Text = "En el servicio gratuito, Google puede usar las capturas para mejorar sus productos. Usá capturas ficticias o anonimizadas; no envíes información personal ni confidencial.", AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.DarkRed });
        var link = new LinkLabel { Text = "Crear clave en Google AI Studio", AutoSize = true };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://aistudio.google.com/api-keys") { UseShellExecute = true });
        panel.Controls.Add(link);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var save = new Button { Text = "Guardar", AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                if (!Regex.IsMatch(model.Text.Trim(), @"^gemini-[a-z0-9.-]+$")) throw new FormatException("Ingresá un nombre válido de modelo Gemini.");
                if (key.Text.Trim().Length > 0) settings.SetApiKey(key.Text.Trim());
                settings.Model = model.Text.Trim();
                settings.DailyLimit = (int)limit.Value;
                settings.FreeProjectConfirmed = confirm.Checked;
                settings.Save();
                key.Clear();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var remove = new Button { Text = "Quitar clave", AutoSize = true };
        remove.Click += (_, _) => { settings.ProtectedKey = ""; settings.Save(); key.Clear(); key.PlaceholderText = "Clave eliminada"; };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { save, remove, cancel });
        panel.Controls.Add(buttons);
        Controls.Add(panel);
        CancelButton = cancel;
    }
}
