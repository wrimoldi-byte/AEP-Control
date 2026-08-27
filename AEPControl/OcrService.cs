using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AEPControl;

public static class OcrService
{
    public static string LastDiagnosticFolder { get; private set; } = string.Empty;
    public static string LastRawText { get; private set; } = string.Empty;

    public static async Task<string> ReadAsync(Bitmap bitmap)
    {
        if (bitmap.Width < 20 || bitmap.Height < 20)
            throw new InvalidOperationException("La zona seleccionada es demasiado pequeña para leerla.");

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AEPControl-Diagnostico");
        Directory.CreateDirectory(folder);
        LastDiagnosticFolder = folder;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var capturePath = Path.Combine(folder, $"captura-{stamp}.png");
        bitmap.Save(capturePath, ImageFormat.Png);

        var candidates = new List<(string Name, Bitmap Image, bool Dispose)>
        {
            ("original", bitmap, false),
            ("gris-2x", Enhance(bitmap, 2, 1.65f, false), true),
            ("alto-contraste-3x", Enhance(bitmap, 3, 2.05f, true), true)
        };

        var results = new List<(string Name, string Language, string Text, int Score)>();
        try
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Dispose)
                    candidate.Image.Save(Path.Combine(folder, $"{candidate.Name}-{stamp}.png"), ImageFormat.Png);

                foreach (var engine in CreateEngines())
                {
                    try
                    {
                        var text = await RecognizeAsync(candidate.Image, engine.Engine);
                        results.Add((candidate.Name, engine.Language, text, Score(text)));
                    }
                    catch (Exception ex)
                    {
                        results.Add((candidate.Name, engine.Language, $"[ERROR OCR: {ex.Message}]", 0));
                    }
                }
            }
        }
        finally
        {
            foreach (var candidate in candidates.Where(c => c.Dispose))
                candidate.Image.Dispose();
        }

        var best = results.OrderByDescending(r => r.Score).FirstOrDefault();
        LastRawText = best.Text ?? string.Empty;

        var report = new List<string>
        {
            $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Captura: {bitmap.Width}x{bitmap.Height}",
            $"Idiomas OCR instalados: {string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag))}",
            ""
        };
        foreach (var result in results.OrderByDescending(r => r.Score))
        {
            report.Add($"=== {result.Name} | {result.Language} | puntaje {result.Score} ===");
            report.Add(result.Text);
            report.Add("");
        }
        File.WriteAllLines(Path.Combine(folder, $"ocr-{stamp}.txt"), report);

        if (string.IsNullOrWhiteSpace(LastRawText))
        {
            throw new InvalidOperationException(
                "El OCR no pudo leer ningún texto. Se guardaron la captura, las imágenes mejoradas y el informe en:\n\n" + folder);
        }

        return LastRawText;
    }

    public static async Task<DepartureOperationData> ReadDepartureOperationAsync(Bitmap bitmap)
    {
        if (bitmap.Width < 120 || bitmap.Height < 80)
            throw new InvalidOperationException("La zona seleccionada es demasiado pequeña. Marcá el cuadro ITO completo.");

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AEPControl-Diagnostico");
        Directory.CreateDirectory(folder);
        LastDiagnosticFolder = folder;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        bitmap.Save(Path.Combine(folder, $"ito-captura-{stamp}.png"), ImageFormat.Png);

        var leftWidth = Math.Max(20, (int)(bitmap.Width * 0.56));
        var rightX = Math.Max(0, (int)(bitmap.Width * 0.43));
        var candidates = new List<(string Name, Bitmap Image, bool Dispose)>
        {
            ("ito-original", bitmap, false),
            ("ito-gris-2x", Enhance(bitmap, 2, 1.45f, false), true),
            ("ito-contraste-3x", Enhance(bitmap, 3, 1.90f, true), true),
            ("ito-datos-aeronave-3x", EnhanceCrop(bitmap, new Rectangle(0, 0, leftWidth, bitmap.Height), 3, 1.55f), true),
            ("ito-servicios-3x", EnhanceCrop(bitmap, new Rectangle(rightX, 0, bitmap.Width - rightX, bitmap.Height), 3, 1.55f), true)
        };

        var engines = CreateEngines().Take(3).ToList();
        var results = new List<(string Name, string Language, string Text)>();
        try
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Dispose)
                    candidate.Image.Save(Path.Combine(folder, $"{candidate.Name}-{stamp}.png"), ImageFormat.Png);

                foreach (var engine in engines)
                {
                    try
                    {
                        var text = await RecognizeAsync(candidate.Image, engine.Engine);
                        if (!string.IsNullOrWhiteSpace(text))
                            results.Add((candidate.Name, engine.Language, text));
                    }
                    catch (Exception ex)
                    {
                        results.Add((candidate.Name, engine.Language, $"[ERROR OCR: {ex.Message}]"));
                    }
                }
            }
        }
        finally
        {
            foreach (var candidate in candidates.Where(candidate => candidate.Dispose))
                candidate.Image.Dispose();
        }

        var validTexts = results
            .Select(result => result.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text) && !text.StartsWith("[ERROR OCR:", StringComparison.Ordinal))
            .ToList();
        LastRawText = string.Join(Environment.NewLine, validTexts);

        if (validTexts.Count == 0)
            throw new InvalidOperationException("El OCR no pudo leer el cuadro ITO. Marcá la pantalla completa con un pequeño margen.");

        var data = DepartureOperationParser.ParseMany(validTexts);
        var report = new List<string>
        {
            $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Captura ITO: {bitmap.Width}x{bitmap.Height}",
            $"Resultado: vuelo={data.Vuelo}; matrícula={data.Matricula}; configuración={data.Configuracion}; servicios={data.Servicios}",
            ""
        };
        foreach (var result in results)
        {
            report.Add($"=== {result.Name} | {result.Language} ===");
            report.Add(result.Text);
            report.Add("");
        }
        File.WriteAllLines(Path.Combine(folder, $"ito-ocr-{stamp}.txt"), report);
        return data;
    }

    public static async Task<string> ReadContinuousAsync(Bitmap bitmap)
    {
        if (bitmap.Width < 20 || bitmap.Height < 20)
            return string.Empty;

        using var enhanced = Enhance(bitmap, 2, 1.65f, false);
        var engine = CreateEngines().FirstOrDefault().Engine;
        if (engine is null)
            throw new InvalidOperationException("Windows no tiene ningún idioma OCR instalado.");

        var text = await RecognizeAsync(enhanced, engine);
        LastRawText = text;
        return text;
    }

    private static IEnumerable<(string Language, OcrEngine Engine)> CreateEngines()
    {
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in new[] { "en-US", "es-AR", "es-ES" })
        {
            var language = OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(l => l.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (language is null || !created.Add(language.LanguageTag)) continue;
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null) yield return (language.LanguageTag, engine);
        }

        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            if (!created.Add(language.LanguageTag)) continue;
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null) yield return (language.LanguageTag, engine);
        }

        if (created.Count == 0)
        {
            var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
            if (fallback is null)
                throw new InvalidOperationException("Windows no tiene ningún idioma OCR instalado.");
            yield return ("perfil-Windows", fallback);
        }
    }

    private static async Task<string> RecognizeAsync(Bitmap bitmap, OcrEngine engine)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        using var randomStream = new InMemoryRandomAccessStream();
        await randomStream.WriteAsync(ms.ToArray().AsBuffer());
        randomStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text?.Trim() ?? string.Empty;
    }

    private static Bitmap Enhance(Bitmap source, int scale, float contrast, bool threshold)
    {
        var target = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format24bppRgb);
        target.SetResolution(144, 144);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.None;

            var offset = (1f - contrast) / 2f;
            var matrix = new ColorMatrix(new[]
            {
                new[] { 0.299f * contrast, 0.299f * contrast, 0.299f * contrast, 0f, 0f },
                new[] { 0.587f * contrast, 0.587f * contrast, 0.587f * contrast, 0f, 0f },
                new[] { 0.114f * contrast, 0.114f * contrast, 0.114f * contrast, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { offset, offset, offset, 0f, 1f }
            });

            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(source,
                new Rectangle(0, 0, target.Width, target.Height),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        if (threshold)
            ApplyThreshold(target, 185);

        return target;
    }

    private static Bitmap EnhanceCrop(Bitmap source, Rectangle area, int scale, float contrast)
    {
        using var crop = source.Clone(area, PixelFormat.Format24bppRgb);
        return Enhance(crop, scale, contrast, false);
    }

    private static void ApplyThreshold(Bitmap bitmap, byte limit)
    {
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var gray = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
            bitmap.SetPixel(x, y, gray < limit ? Color.Black : Color.White);
        }
    }

    private static int Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var lettersAndDigits = text.Count(char.IsLetterOrDigit);
        var separators = text.Count(c => c is '/' or '\\' or ':' or '*');
        var likelyFlights = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"\b(?:LA\s*)?\d{3,4}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        return lettersAndDigits + separators * 6 + likelyFlights * 40;
    }
}
