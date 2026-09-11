// Stroke = entidade vetorial imutável após commit (exceto Bounds, que é cache).
// Id sequencial int (não Guid): 16 bytes por stroke não se pagam em milhares de strokes.

namespace EpicPencil.Core;

public sealed class Stroke
{
    public int Id { get; }
    public ToolKind Tool { get; }
    public Rgba Color { get; }
    public float WidthPx { get; } // px globais da tela virtual (espessura física)
    public float Opacity { get; }
    public List<Pt> Points { get; }
    public RectD Bounds { get; private set; }

    public Stroke(int id, ToolKind tool, Rgba color, float widthPx, float opacity, List<Pt> points)
    {
        if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
        if (points.Count == 0) throw new ArgumentException("Stroke precisa de ao menos 1 ponto.", nameof(points));
        Id = id;
        Tool = tool;
        Color = color;
        WidthPx = widthPx;
        Opacity = Math.Clamp(opacity, 0f, 1f);
        Points = points;
        Bounds = ComputeBounds(points, widthPx);
    }

    public static RectD ComputeBounds(List<Pt> points, float widthPx)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        float pad = widthPx / 2 + 2; // +2 px: antialiasing + tolerância de hit-test base
        return new RectD(minX - pad, minY - pad, (maxX - minX) + pad * 2, (maxY - minY) + pad * 2);
    }

    public int PointCount => Points.Count;
}
