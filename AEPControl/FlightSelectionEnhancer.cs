using System.Reflection;

namespace AEPControl;

internal static class FlightSelectionEnhancer
{
    public static void Attach(BubbleMainForm form)
    {
        var type = typeof(BubbleMainForm);
        var gridField = type.GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic);
        var batchField = type.GetField("_batch", BindingFlags.Instance | BindingFlags.NonPublic);
        var indexField = type.GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic);
        var chooseField = type.GetField("_chooseStartFromSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        var stageField = type.GetField("_stage", BindingFlags.Instance | BindingFlags.NonPublic);
        var prepareMethod = type.GetMethod("PrepareFlight", BindingFlags.Instance | BindingFlags.NonPublic);

        if (gridField?.GetValue(form) is not DataGridView grid ||
            batchField?.GetValue(form) is not List<FlightData> batch ||
            indexField is null || chooseField is null || stageField is null || prepareMethod is null)
            return;

        var handling = false;

        grid.CellClick += (_, e) =>
        {
            if (handling || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                return;

            var stage = (int)(stageField.GetValue(form) ?? 0);
            if (stage is not (1 or 3))
                return;

            if (grid.Rows[e.RowIndex].DataBoundItem is not FlightData selected)
                return;

            var selectedIndex = batch.IndexOf(selected);
            if (selectedIndex < 0)
                return;

            handling = true;
            try
            {
                // Cada vez que el operador elige una fila, ese vuelo pasa a ser el
                // destino de la próxima lectura OCR. No sirve sólo para elegir dónde
                // empezar: se puede saltar a cualquier vuelo en cualquier momento.
                indexField.SetValue(form, selectedIndex);
                chooseField.SetValue(form, true);
                prepareMethod.Invoke(form, null);
            }
            finally
            {
                handling = false;
            }
        };
    }
}
