using System.Drawing;
using System.Drawing.Drawing2D;

namespace ImageStitchMaster;

/// <summary>可缩放/平移的图片预览框：滚轮以鼠标为锚点缩放，左键拖动平移，双击复位。</summary>
public sealed class ZoomablePictureBox : PictureBox
{
    private float _fitScale = 1f;
    private Size _lastClientSize;

    public ZoomablePictureBox()
    {
        DoubleBuffered = true;
    }

    public float Zoom { get; private set; } = 1f;
    public PointF Offset { get; private set; }

    public void ResetView()
    {
        Zoom = 1f;
        ComputeFitScale();
        Offset = Image != null
            ? new PointF((Width - Image.Width * _fitScale) / 2f, (Height - Image.Height * _fitScale) / 2f)
            : PointF.Empty;
        _lastClientSize = ClientSize;
        Invalidate();
    }

    public void ZoomAt(PointF mouse, float factor)
    {
        if (Image == null) return;
        // 仅留防御性边界避免浮点溢出/除零，实际范围足够宽，用户感知为无限制
        double newZoom = Math.Clamp(Zoom * factor, 0.01, 1000.0);
        if (Math.Abs(newZoom - Zoom) < 1e-4) return;
        // 保持鼠标位置的图片内容不动：offset' = mouse - (mouse - offset) * (newZoom / zoom)
        Offset = new PointF(
            mouse.X - (mouse.X - Offset.X) * (float)(newZoom / Zoom),
            mouse.Y - (mouse.Y - Offset.Y) * (float)(newZoom / Zoom));
        Zoom = (float)newZoom;
        Invalidate();
    }

    public void PanBy(PointF delta)
    {
        Offset = new PointF(Offset.X + delta.X, Offset.Y + delta.Y);
        Invalidate();
    }

    public void HandleResize()
    {
        var old = _lastClientSize;
        ComputeFitScale();
        if (old.Width > 0 && old.Height > 0)
            Offset = new PointF(Offset.X + (Width - old.Width) / 2f, Offset.Y + (Height - old.Height) / 2f);
        _lastClientSize = ClientSize;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var img = Image;
        if (img == null) return;
        float s = _fitScale * Zoom;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(img, Offset.X, Offset.Y, img.Width * s, img.Height * s);
    }

    private void ComputeFitScale()
    {
        _fitScale = Image != null && Width > 0 && Height > 0
            ? Math.Min(Width / (float)Image.Width, Height / (float)Image.Height)
            : 1f;
    }
}
