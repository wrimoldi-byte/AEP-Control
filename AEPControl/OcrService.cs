using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AEPControl;

public static class OcrService
{
    public static async Task<string> ReadAsync(Bitmap bitmap)
    {
        var original = await RecognizeAsync(bitmap);

        using var enhanced = Enhance(bitmap);
        var improved = await RecognizeAsync(enhanced);

        return Score(improved) >= Score(original) ? improved : original;
    }

    private static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        using var randomStream = new InMemoryRandomAccessStream();
        await randomStream.WriteAsync(ms.ToArray().AsBuffer());
        randomStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException("Windows no tiene un idioma OCR instalado.");
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text ?? string.Empty;
    }

    private static Bitmap Enhance(Bitmap source)
    {
        var scale = source.Width < 1800 ? 2 : 1;
        var target = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format24bppRgb);
        target.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        var contrast = 1.45f;
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

        return target;
    }

    private static int Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var lettersAndDigits = text.Count(char.IsLetterOrDigit);
        var separators = text.Count(c => c is '/' or '\\' or ':');
        return lettersAndDigits + separators * 5;
    }
}
