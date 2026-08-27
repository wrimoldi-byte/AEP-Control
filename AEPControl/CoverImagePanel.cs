using System.Drawing.Drawing2D;

namespace AEPControl;

public sealed class CoverImagePanel : Panel
{
    public Image? HeroImage { get; set; }
    public float VerticalFocus { get; set; } = 0.30f;

    public CoverImagePanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(18, 42, 70));
        if (HeroImage is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        var targetRatio = ClientSize.Width / (float)ClientSize.Height;
        var imageRatio = HeroImage.Width / (float)HeroImage.Height;
        RectangleF source;

        if (imageRatio > targetRatio)
        {
            var width = HeroImage.Height * targetRatio;
            source = new RectangleF((HeroImage.Width - width) / 2f, 0, width, HeroImage.Height);
        }
        else
        {
            var height = HeroImage.Width / targetRatio;
            var available = Math.Max(0, HeroImage.Height - height);
            var top = available * Math.Clamp(VerticalFocus, 0f, 1f);
            source = new RectangleF(0, top, HeroImage.Width, height);
        }

        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var destination = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        e.Graphics.DrawImage(HeroImage, destination, source, GraphicsUnit.Pixel);
        using var overlay = new SolidBrush(Color.FromArgb(62, 10, 31, 55));
        e.Graphics.FillRectangle(overlay, ClientRectangle);
    }
}
