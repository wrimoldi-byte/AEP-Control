namespace AEPControl;

/// <summary>
/// Palabras del OCR de Windows con la posición dentro de la imagen ampliada.
/// Conservamos las coordenadas para reconstruir las filas de Sabre:
/// el nombre y BKUP pueden pertenecer a líneas OCR distintas por la distancia entre columnas.
/// </summary>
public readonly record struct CdcOcrWord(string Text, double X, double Y, double Width, double Height);

public static class CdcOcrLayout
{
    private sealed class VisualLine
    {
        public readonly List<CdcOcrWord> Words = new();
        public double CenterY { get; private set; }

        public void Add(CdcOcrWord word)
        {
            var middle = word.Y + word.Height / 2.0;
            CenterY = (CenterY * Words.Count + middle) / (Words.Count + 1);
            Words.Add(word);
        }
    }

    /// <summary>
    /// Recompone líneas por coordenada vertical, no por bloques de texto del OCR.
    /// BKUP se asocia a un nombre únicamente si ambos comparten la misma línea
    /// visual; los EDITS de otra fila no se atribuyen al pasajero anterior.
    /// </summary>
    public static string BuildRows(IEnumerable<CdcOcrWord> words)
    {
        var valid = words.Where(w => !string.IsNullOrWhiteSpace(w.Text) &&
            w.Height > 0 && w.Width > 0).ToList();
        if (valid.Count == 0) return string.Empty;

        var heights = valid.Select(w => w.Height).OrderBy(h => h).ToArray();
        var medianHeight = heights[heights.Length / 2];
        var tolerance = Math.Clamp(medianHeight * 0.70, 5, 25);
        var lines = new List<VisualLine>();

        foreach (var word in valid.OrderBy(w => w.Y + w.Height / 2.0))
        {
            var center = word.Y + word.Height / 2.0;
            VisualLine? closest = null;
            var closestDistance = double.MaxValue;
            foreach (var line in lines)
            {
                var distance = Math.Abs(line.CenterY - center);
                if (distance <= tolerance && distance < closestDistance)
                {
                    closest = line;
                    closestDistance = distance;
                }
            }

            if (closest is null)
            {
                closest = new VisualLine();
                lines.Add(closest);
            }

            closest.Add(word);
        }

        return string.Join("\n", lines.OrderBy(l => l.CenterY)
            .Select(l => string.Join(" ", l.Words.OrderBy(w => w.X)
                .Select(w => w.Text.Trim()))));
    }
}
