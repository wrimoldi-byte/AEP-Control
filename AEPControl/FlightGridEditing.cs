namespace AEPControl;

// Keep FlightData as the single source of truth, including computed Booking/EDITS.
// Virtual, unbound columns allow those values to be edited without adding OCR setters.
public static class FlightGridEditing
{
    // EndEdit() alone does not raise CellValidating when invoked by toolbar actions.
    public static bool FinishEdit(DataGridView grid)
    {
        if (grid.IsCurrentCellDirty && grid.EditingControl is TextBox editor)
        {
            try
            {
                FlightCorrections.NormalizeManualValue((string)grid.CurrentCell.OwningColumn.Tag!, editor.Text);
            }
            catch (FormatException ex)
            {
                grid.CurrentCell.ErrorText = ex.Message;
                grid.Focus();
                return false;
            }
        }
        return grid.EndEdit();
    }

    public static void Attach(DataGridView grid, Action<FlightData> saved, Action<string> status)
    {
        grid.ReadOnly = false;
        grid.VirtualMode = true;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Name = column.DataPropertyName;
            column.Tag = column.DataPropertyName;
            column.DataPropertyName = "";
            column.ReadOnly = column.Name == nameof(FlightData.Revision);
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.MinimumWidth = column.Name == nameof(FlightData.Edits) ? 210 : 75;
            column.ToolTipText = column.ReadOnly ? "Correcciones protegidas frente a nuevas lecturas."
                : column.Name == nameof(FlightData.Edits)
                    ? "Doble clic para editar. Ejemplo: WCHR 2; INF 1; ETO 3. Borrá un código para quitarlo. WCHR 0 confirma cero."
                    : "Doble clic o F2 para editar. Enter/Tab confirma; Esc cancela.";
        }
        grid.CellValueNeeded += (_, e) =>
        {
            if (grid.Rows[e.RowIndex].DataBoundItem is FlightData flight)
                e.Value = typeof(FlightData).GetProperty((string)grid.Columns[e.ColumnIndex].Tag!)!.GetValue(flight);
        };
        grid.CellValidating += (_, e) =>
        {
            if (!grid.IsCurrentCellDirty || grid.Columns[e.ColumnIndex].ReadOnly) return;
            try
            {
                FlightCorrections.NormalizeManualValue((string)grid.Columns[e.ColumnIndex].Tag!, Convert.ToString(e.FormattedValue) ?? "");
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
            }
            catch (FormatException ex)
            {
                e.Cancel = true;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = ex.Message;
                status(ex.Message + " Corregí el dato o presioná Esc para cancelar.");
            }
        };
        grid.CellValuePushed += (_, e) =>
        {
            if (grid.Rows[e.RowIndex].DataBoundItem is not FlightData flight) return;
            try
            {
                FlightCorrections.SetManualValue(flight, (string)grid.Columns[e.ColumnIndex].Tag!, Convert.ToString(e.Value) ?? "");
                grid.InvalidateRow(e.RowIndex);
                saved(flight);
            }
            catch (FormatException ex)
            {
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = ex.Message;
                status(ex.Message + " No se modificó el valor anterior.");
            }
        };
        grid.CellEndEdit += (_, e) =>
        {
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
            grid.InvalidateRow(e.RowIndex);
        };
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !grid.Columns[e.ColumnIndex].ReadOnly) grid.BeginEdit(true);
        };
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
            status("No se guardó el cambio. Revisá el formato o presioná Esc para cancelar.");
        };
    }
}
