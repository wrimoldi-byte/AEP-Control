namespace AEPControl;

public sealed class PassengerDocumentsForm : Form
{
    public PassengerDocumentsForm(IReadOnlyList<PassengerDocument> documents)
    {
        Text = $"Documentación de PAX — {documents.Count} detectado(s)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 480);
        MinimumSize = new Size(900, 360);
        TopMost = true;

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 10, 12, 0),
            Text = "Revisá los datos detectados antes de utilizarlos. El número de documento se muestra parcialmente oculto."
        };

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
        Controls.Add(notice);
    }

    private static void AddColumn(DataGridView grid, string title, string property, float weight) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            DataPropertyName = property,
            FillWeight = weight
        });
}
