using System.Drawing;
using System.Drawing.Drawing2D;

namespace ImageStitchMaster;

/// <summary>
/// 可缩放/平移的图片预览框。默认（未缩放）沿用系统 PictureBoxSizeMode.Zoom 适配，
/// 让 WinForms 处理等比缩放与高 DPI 对齐，避免手写比例导致右/下边缘被裁；
/// 用户滚轮放大后才进入手绘平移路径。
/// </summary>
public sealed class ZoomablePictureBox : PictureBox
{
    private float _fitScale = 1f;
    private Size _lastClientSize;

    public ZoomablePictureBox()
    {
        DoubleBuffered = true;
        // 默认按系统适配方式渲染（即 v1.0.1 的 PictureBoxSizeMode.Zoom，不会缺边）
        SizeMode = PictureBoxSizeMode.Zoom;
    }

    public float Zoom { get; private set; } = 1f;
    public PointF Offset { get; private set; }

    public void ResetView()
    {
        Zoom = 1f;
        ComputeFitScale();
        Offset = Image != null
            ? new PointF((ClientSize.Width - Image.Width * _fitScale) / 2f, (ClientSize.Height - Image.Height * _fitScale) / 2f)
            : PointF.Empty;
        _lastClientSize = ClientSize;
        Invalidate();
    }

    public void ZoomAt(PointF mouse, float factor)
    {
        if (Image == null) return;
        double newZoom = Math.Clamp(Zoom * factor, 1.0, 1000.0);
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
            Offset = new PointF(Offset.X + (ClientSize.Width - old.Width) / 2f, Offset.Y + (ClientSize.Height - old.Height) / 2f);
        _lastClientSize = ClientSize;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var img = Image;
        if (img == null)
        {
            e.Graphics.Clear(BackColor);
            return;
        }

        // 未缩放时交给系统 SizeMode=Zoom 渲染（与 v1.0.1 一致，DPI/等比适配由 WinForms 保证，不缺边）
        if (Zoom <= 1f)
        {
            base.OnPaint(e);
            return;
        }

        // 放大后手绘：以适配尺寸为基准放大，并支持鼠标锚点缩放与拖拽平移
        e.Graphics.Clear(BackColor);
        float s = _fitScale * Zoom;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(img, Offset.X, Offset.Y, img.Width * s, img.Height * s);
    }

    private void ComputeFitScale()
    {
        _fitScale = Image != null && ClientSize.Width > 0 && ClientSize.Height > 0
            ? Math.Min(ClientSize.Width / (float)Image.Width, ClientSize.Height / (float)Image.Height)
            : 1f;
    }
}
