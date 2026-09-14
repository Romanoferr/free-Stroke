// Formas geométricas como objetos paramétricos do documento (não strokes).
// Somente contorno, sem preenchimento (REQ3). Posição/tamanho em PX GLOBAIS
// da tela virtual, mesmo espaço de strokes/textos/capturas.
// Mutação de X/Y SOMENTE via MoveShapeCommand — nunca direto pela Shell.
//
// Decisão Circle (REQ3): o CircleObject guarda o retângulo delimitador e é
// renderizado/selecionado como a ELIPSE inscrita nele. Motivo: o gesto de
// criação (drag define o bbox, qualquer direção) fica idêntico ao do
// retângulo, consistente com o sistema atual; círculo perfeito exigiria
// constraint com modificador, fora do escopo. Com bbox quadrado, a elipse
// É um círculo perfeito.

namespace EpicPencil.Core;

// Posição mutável via comando genérico (MoveShapeCommand). Text/Screen
// seguem nos comandos específicos históricos; novas formas usam esta.
public interface IMovableShape : ICanvasObject
{
    float X { get; set; }
    float Y { get; set; }
}

public sealed class RectangleObject : IMovableShape
{
    public int Id { get; }
    public CanvasObjectKind Kind => CanvasObjectKind.Rectangle;
    public float X { get; set; } // mutado SOMENTE via MoveShapeCommand
    public float Y { get; set; } // mutado SOMENTE via MoveShapeCommand
    public float WidthPx { get; }
    public float HeightPx { get; }
    public Rgba Color { get; }
    public float StrokeWidthPx { get; } // contorno, px globais

    public RectangleObject(int id, float x, float y, float widthPx, float heightPx,
        Rgba color, float strokeWidthPx)
    {
        if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException("bbox inválido");
        if (strokeWidthPx <= 0) throw new ArgumentOutOfRangeException(nameof(strokeWidthPx));
        Id = id;
        X = x; Y = y;
        WidthPx = widthPx; HeightPx = heightPx;
        Color = color;
        StrokeWidthPx = strokeWidthPx;
    }

    public RectD Bounds => new(X, Y, WidthPx, HeightPx);

    public bool HitTest(Pt p) => ShapeGeometry.HitRectangle(this, p);
}

public sealed class CircleObject : IMovableShape
{
    public int Id { get; }
    public CanvasObjectKind Kind => CanvasObjectKind.Circle;
    public float X { get; set; } // bbox, mutado SOMENTE via MoveShapeCommand
    public float Y { get; set; } // bbox, mutado SOMENTE via MoveShapeCommand
    public float WidthPx { get; } // bbox (elipse inscrita; quadrado = círculo)
    public float HeightPx { get; }
    public Rgba Color { get; }
    public float StrokeWidthPx { get; } // contorno, px globais

    public CircleObject(int id, float x, float y, float widthPx, float heightPx,
        Rgba color, float strokeWidthPx)
    {
        if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException("bbox inválido");
        if (strokeWidthPx <= 0) throw new ArgumentOutOfRangeException(nameof(strokeWidthPx));
        Id = id;
        X = x; Y = y;
        WidthPx = widthPx; HeightPx = heightPx;
        Color = color;
        StrokeWidthPx = strokeWidthPx;
    }

    public RectD Bounds => new(X, Y, WidthPx, HeightPx);

    public bool HitTest(Pt p) => ShapeGeometry.HitCircle(this, p);
}

// Geometria pura das formas (Core, sem UI): normalização do drag,
// tolerância e hit-test de CONTORNO (nunca Bounds.Contains — o interior
// vazio não seleciona, REQ6). Reutiliza Geometry.DistancePointToSegment.
public static class ShapeGeometry
{
    // Lado mínimo do bbox p/ commit (mesmo limiar do marquee: 4 px).
    // Abaixo disso = clique sem arrasto → cancelado, sem objeto e sem undo.
    public const float MinSizePx = 4f;

    // Segmentos na aproximação poligonal da elipse (hit-test do círculo).
    private const int EllipseSegments = 64;

    public static RectD Normalize(Pt a, Pt b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public static bool MeetsMinSize(RectD r) =>
        r.Width >= MinSizePx && r.Height >= MinSizePx;

    // Tolerância de seleção do contorno: facilita o mouse sem exigir
    // precisão cirúrgica (mesma ordem do EraseTolerance dos strokes).
    public static float Tolerance(float strokeWidthPx) =>
        Math.Max(6f, strokeWidthPx / 2f + 2f);

    public static bool HitRectangle(RectangleObject r, Pt p) =>
        HitRectContour(new RectD(r.X, r.Y, r.WidthPx, r.HeightPx), r.StrokeWidthPx, p);

    public static bool HitCircle(CircleObject c, Pt p) =>
        HitEllipseContour(new RectD(c.X, c.Y, c.WidthPx, c.HeightPx), c.StrokeWidthPx, p);

    internal static bool HitRectContour(RectD r, float strokeWidthPx, Pt p)
    {
        float tol = Tolerance(strokeWidthPx);
        // Early-out barato antes das 4 distâncias exatas.
        if (!r.Inflate(tol).Contains(p)) return false;
        var tl = new Pt(r.X, r.Y);
        var tr = new Pt(r.Right, r.Y);
        var br = new Pt(r.Right, r.Bottom);
        var bl = new Pt(r.X, r.Bottom);
        return Geometry.DistancePointToSegment(p, tl, tr) <= tol
            || Geometry.DistancePointToSegment(p, tr, br) <= tol
            || Geometry.DistancePointToSegment(p, br, bl) <= tol
            || Geometry.DistancePointToSegment(p, bl, tl) <= tol;
    }

    internal static bool HitEllipseContour(RectD r, float strokeWidthPx, Pt p)
    {
        float tol = Tolerance(strokeWidthPx);
        if (!r.Inflate(tol).Contains(p)) return false;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float rx = r.Width / 2f, ry = r.Height / 2f;
        Pt prev = new(cx + rx, cy);
        float best = float.MaxValue;
        for (int i = 1; i <= EllipseSegments; i++)
        {
            float a = i * MathF.PI * 2f / EllipseSegments;
            var cur = new Pt(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
            float d = Geometry.DistancePointToSegment(p, prev, cur);
            if (d < best) best = d;
            prev = cur;
        }
        return best <= tol;
    }
}
