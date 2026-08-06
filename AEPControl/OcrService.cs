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
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        using var randomStream = new InMemoryRandomAccessStream();
        await randomStream.WriteAsync(ms.ToArray().AsBuffer());
        randomStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException("Windows no tiene un idioma OCR instalado.");
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text;
    }
}
