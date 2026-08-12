namespace AEPControl;

public sealed class PassengerDocumentsForm : Form
{
    private readonly IReadOnlyList<PassengerDocument> _documents;

    public PassengerDocumentsForm(IReadOnlyList<PassengerDocument> documents)
    {
        _documents = documents;
        Text = $"Documentación de PAX — {documents.Count} detectado(s)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 540);
        MinimumSize = new Size(900, 400);
        TopMost = true;

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 10, 12, 0),
            Text = "Revisá los datos detectados antes de utilizarlos. El número de documento se muestra parcialmente oculto en pantalla."
        };

        var exportButton = new Button
        {
            Text = "Exportar documentación a Excel",
            AutoSize = true,
            Padding = new Padding(12, 7, 12, 7)
        };
        exportButton.Click += (_, _) => ExportToExcel();

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12, 6, 12, 4)
        };
        bar.Controls.Add(exportButton);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            DataSource = documents.ToList()
        };

        AddColumn(grid, "Tipo", nameof(PassengerDocument.DocumentTypeName), 70);
        AddColumn(grid, "Emisor", nameof(PassengerDocument.IssuingCountry), 48);
        AddColumn(grid, "Documento", nameof(PassengerDocument.MaskedDocumentNumber), 80);
        AddColumn(grid, "Nacionalidad", nameof(PassengerDocument.Nationality), 58);
        AddColumn(grid, "Nacimiento", nameof(PassengerDocument.BirthDateText), 72);
        AddColumn(grid, "Sexo", nameof(PassengerDocument.Sex), 38);
        AddColumn(grid, "Vencimiento", nameof(PassengerDocument.ExpirationDateText), 72);
        AddColumn(grid, "Apellido", nameof(PassengerDocument.Surname), 110);
        AddColumn(grid, "Nombres", nameof(PassengerDocument.GivenNames), 120);

        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(notice);
    }

    private void ExportToExcel()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Archivo Excel (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = $"Documentacion-PAX-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            PassengerDocumentExcelExporter.Export(dialog.FileName, _documents);
            MessageBox.Show(
                $"Excel generado correctamente:\n\n{dialog.FileName}\n\nEl archivo incluye el número completo de documento leído.",
                "Documentación de PAX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo generar el Excel:\n\n" + ex.Message,
                "Documentación de PAX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void AddColumn(DataGridView grid, string title, string property, float weight) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            DataPropertyName = property,
            FillWeight = weight
        });
}
