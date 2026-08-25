using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ImageStitchMaster;

public sealed class ImageItem : IDisposable
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public int Width { get; }
    public int Height { get; }
    public Bitmap Thumbnail { get; }

    private ImageItem(string filePath, int width, int height, Bitmap thumbnail)
    {
        FilePath = filePath;
        Width = width;
        Height = height;
        Thumbnail = thumbnail;
    }

    public static ImageItem Load(string filePath, int thumbMaxSide = 512)
    {
        using var src = StitchEngine.LoadBitmap(filePath);
        var thumb = StitchEngine.Resize(src, thumbMaxSide);
        return new ImageItem(filePath, src.Width, src.Height, thumb);
    }

    public void Dispose() => Thumbnail.Dispose();
}

public sealed record LayoutResult(Size Canvas, IReadOnlyList<Rectangle> Rects, bool Clamped);

public sealed record SaveResult(
    string FinalPath, string Format, int Quality,
    Size FinalSize, long Bytes, bool ConvertedToJpg, bool Downscaled);

/// <summary>最终编码结果：字节数据 + 元信息（写文件由调用方负责）。</summary>
public sealed record SavePlan(byte[] Data, SaveResult Meta);

public static class StitchEngine
{
    // GDI+ 单边尺寸上限
    private const int MaxDimension = 65500;
    private const int MinQuality = 60;
    private const int MaxQuality = 95;
    private const int DefaultQuality = 90;

    public static Bitmap LoadBitmap(string path)
    {
        // 经字节流加载，避免 Bitmap(path) 长期锁定源文件
        var bytes = File.ReadAllBytes(path);
        var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    public static LayoutResult ComputeLayout(IReadOnlyList<ImageItem> items, bool vertical)
    {
        if (items.Count == 0) throw new ArgumentException("没有图片");

        int baseSize = vertical ? items.Min(i => i.Width) : items.Min(i => i.Height);
        var lengths = items.Select(i => vertical
            ? (int)Math.Max(1, Math.Round(i.Height * (double)baseSize / i.Width))
            : (int)Math.Max(1, Math.Round(i.Width * (double)baseSize / i.Height))).ToArray();

        long total = lengths.Sum(l => (long)l);
        bool clamped = false;
        if (total > MaxDimension)
        {
            double f = MaxDimension / (double)total;
            baseSize = Math.Max(1, (int)(baseSize * f));
            for (int i = 0; i < lengths.Length; i++)
                lengths[i] = Math.Max(1, (int)(lengths[i] * f));
            total = lengths.Sum(l => (long)l);
            clamped = true;
        }

        var rects = new List<Rectangle>(items.Count);
        int offset = 0;
        foreach (var len in lengths)
        {
            rects.Add(vertical
                ? new Rectangle(0, offset, baseSize, len)
                : new Rectangle(offset, 0, len, baseSize));
            offset += len;
        }

        var canvas = vertical ? new Size(baseSize, (int)total) : new Size((int)total, baseSize);
        return new LayoutResult(canvas, rects, clamped);
    }

    /// <summary>用缓存缩略图快速合成预览（低内存）。</summary>
    public static Bitmap RenderPreview(IReadOnlyList<ImageItem> items, bool vertical, int maxLongSide = 1600)
    {
        var layout = ComputeLayout(items, vertical);
        double f = Math.Min(1.0, maxLongSide / (double)Math.Max(layout.Canvas.Width, layout.Canvas.Height));
        var size = new Size(Math.Max(1, (int)(layout.Canvas.Width * f)), Math.Max(1, (int)(layout.Canvas.Height * f)));

        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var g = CreateGraphics(bmp);
        for (int i = 0; i < items.Count; i++)
        {
            var r = layout.Rects[i];
            var dest = new Rectangle(
                (int)Math.Round(r.X * f), (int)Math.Round(r.Y * f),
                Math.Max(1, (int)Math.Round(r.Width * f)), Math.Max(1, (int)Math.Round(r.Height * f)));
            DrawScaled(g, items[i].Thumbnail, dest);
        }
        return bmp;
    }

    public enum RenderMode { Normal, Parallel }

    /// <summary>全分辨率合成。普通模式逐张解码+绘制（内存友好）；并行模式多核解码（更快，内存峰值略高）。</summary>
    public static Bitmap RenderFull(IReadOnlyList<ImageItem> items, bool vertical, IProgress<(int done, int total)>? progress = null, RenderMode mode = RenderMode.Normal)
    {
        var layout = ComputeLayout(items, vertical);
        var bmp = new Bitmap(layout.Canvas.Width, layout.Canvas.Height, PixelFormat.Format24bppRgb);
        using var g = CreateGraphics(bmp);
        try
        {
            if (mode == RenderMode.Parallel)
                RenderParallel(g, items, layout, progress);
            else
                RenderSequential(g, items, layout, progress);
            return bmp;
        }
        catch
        {
            // 合成中途失败也要释放画布，避免大位图滞留
            bmp.Dispose();
            throw;
        }
    }

    private static void RenderSequential(Graphics g, IReadOnlyList<ImageItem> items, LayoutResult layout, IProgress<(int done, int total)>? progress)
    {
        for (int i = 0; i < items.Count; i++)
        {
            using var src = LoadBitmap(items[i].FilePath);
            DrawScaled(g, src, layout.Rects[i]);
            progress?.Report((i + 1, items.Count));
        }
    }

    private static void RenderParallel(Graphics g, IReadOnlyList<ImageItem> items, LayoutResult layout, IProgress<(int done, int total)>? progress)
    {
        int batch = Math.Max(1, Environment.ProcessorCount * 2);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        for (int start = 0; start < items.Count; start += batch)
        {
            int count = Math.Min(batch, items.Count - start);
            var srcs = new Bitmap[count];
            try
            {
                Parallel.For(0, count, parallel, j => srcs[j] = LoadBitmap(items[start + j].FilePath));
                for (int j = 0; j < count; j++)
                {
                    DrawScaled(g, srcs[j], layout.Rects[start + j]);
                    progress?.Report((start + j + 1, items.Count));
                }
            }
            finally
            {
                // 无论正常还是解码中途抛异常，本批已解码的位图都必须释放
                foreach (var b in srcs) b?.Dispose();
            }
        }
    }

    /// <summary>编码生成最终字节数据（不写文件），便于先展示预计体积再保存。</summary>
    public static SavePlan CreatePlan(Bitmap bmp, string path, bool asPng, long? limitBytes, Action<string>? status = null)
    {
        if (asPng)
        {
            if (limitBytes is null)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                var data = ms.ToArray();
                return new SavePlan(data, new SaveResult(path, "PNG", 100, bmp.Size, data.LongLength, false, false));
            }

            status?.Invoke("正在编码 PNG…");
            using var ms2 = new MemoryStream();
            bmp.Save(ms2, ImageFormat.Png);
            if (ms2.Length <= limitBytes)
            {
                var data = ms2.ToArray();
                return new SavePlan(data, new SaveResult(path, "PNG", 100, bmp.Size, data.LongLength, false, false));
            }
            // PNG 超限，自动转 JPG 压缩
            path = Path.ChangeExtension(path, ".jpg");
            var (jpegData, jpegMeta) = SaveJpegWithLimit(bmp, path, limitBytes.Value, status);
            return new SavePlan(jpegData, jpegMeta with { ConvertedToJpg = true });
        }

        if (limitBytes is null)
        {
            var bytes = EncodeJpeg(bmp, DefaultQuality);
            return new SavePlan(bytes, new SaveResult(path, "JPG", DefaultQuality, bmp.Size, bytes.LongLength, false, false));
        }
        var (data2, meta2) = SaveJpegWithLimit(bmp, path, limitBytes.Value, status);
        return new SavePlan(data2, meta2);
    }

    /// <summary>渲染预览图，并对高分辨率采样图编码，按幂律外推全尺寸 JPG 体积（自适应内容复杂度/清晰度）。</summary>
    public static (Bitmap preview, long estimate) RenderPreviewWithEstimate(
        IReadOnlyList<ImageItem> items, bool vertical, Size fullCanvas,
        int previewMaxSide = 1600, int sampleMaxSide = 3200)
    {
        var preview = RenderPreview(items, vertical, previewMaxSide);
        int fullSide = Math.Max(fullCanvas.Width, fullCanvas.Height);
        int sampleSide = Math.Min(sampleMaxSide, fullSide);
        long lowBytes = EncodeJpeg(preview, DefaultQuality).LongLength;

        // 全尺寸接近或等于预览尺寸：预览图即全尺寸，估算直接准确
        if (sampleSide <= previewMaxSide)
            return (preview, lowBytes);

        long highBytes;
        using (var sample = RenderPreview(items, vertical, sampleSide))
            highBytes = EncodeJpeg(sample, DefaultQuality).LongLength;

        // JPG 体积 ∝ 边长^β，β 由两个采样点拟合，外推到全尺寸；越清晰/细节越多 β 越大
        double beta = 1.0;
        if (lowBytes > 0 && highBytes > lowBytes)
            beta = Math.Log((double)highBytes / lowBytes) / Math.Log((double)sampleSide / previewMaxSide);
        if (double.IsNaN(beta) || beta < 0.8 || beta > 2.5) beta = 1.5;
        long est = (long)(highBytes * Math.Pow((double)fullSide / sampleSide, beta));
        return (preview, est);
    }

    /// <summary>先质量二分（95→60），仍超限则等比缩小分辨率重试。</summary>
    private static (byte[] data, SaveResult meta) SaveJpegWithLimit(Bitmap original, string path, long limit, Action<string>? status)
    {
        Bitmap cur = original;
        bool downscaled = false;
        try
        {
            while (true)
            {
                status?.Invoke($"正在压缩（当前 {cur.Width}×{cur.Height}）…");
                var atMin = EncodeJpeg(cur, MinQuality);
                if (atMin.LongLength <= limit)
                {
                    // 最低质量已达标，二分找满足上限的最高质量
                    int lo = MinQuality, hi = MaxQuality, bestQ = MinQuality;
                    byte[] best = atMin;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        var data = EncodeJpeg(cur, mid);
                        if (data.LongLength <= limit) { bestQ = mid; best = data; lo = mid + 1; }
                        else hi = mid - 1;
                    }
                    return (best, new SaveResult(path, "JPG", bestQ, cur.Size, best.LongLength, false, downscaled));
                }

                int longSide = Math.Max(cur.Width, cur.Height);
                if (longSide <= 200)
                    return (atMin, new SaveResult(path, "JPG", MinQuality, cur.Size, atMin.LongLength, false, downscaled));

                double f = Math.Sqrt(limit / (double)atMin.LongLength) * 0.95;
                int newSide = Math.Max(200, (int)(longSide * Math.Min(f, 0.9)));
                var next = Resize(cur, newSide);
                if (!ReferenceEquals(cur, original)) cur.Dispose();
                cur = next;
                downscaled = true;
            }
        }
        finally
        {
            if (!ReferenceEquals(cur, original)) cur.Dispose();
        }
    }

    public static byte[] EncodeJpeg(Bitmap bmp, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, ep);
        return ms.ToArray();
    }

    public static Bitmap Resize(Bitmap src, int maxLongSide)
    {
        double f = Math.Min(1.0, maxLongSide / (double)Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)Math.Round(src.Width * f));
        int h = Math.Max(1, (int)Math.Round(src.Height * f));
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = CreateGraphics(bmp);
        DrawScaled(g, src, new Rectangle(0, 0, w, h));
        return bmp;
    }

    private static Graphics CreateGraphics(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        return g;
    }

    private static void DrawScaled(Graphics g, Image src, Rectangle dest)
    {
        // 拼接常见等宽/等高场景：尺寸相同或无/微缩放时走快速绘制路径，避免高质量插值开销
        if (dest.Width == src.Width && dest.Height == src.Height)
        {
            g.DrawImageUnscaled(src, dest.Location);
            return;
        }
        if (Math.Abs(dest.Width - src.Width) <= 2 && Math.Abs(dest.Height - src.Height) <= 2)
        {
            g.DrawImage(src, dest);
            return;
        }
        // TileFlipXY 避免高质量插值在图片边缘产生半透明缝隙
        using var attr = new ImageAttributes();
        attr.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, dest, 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
    }
}
