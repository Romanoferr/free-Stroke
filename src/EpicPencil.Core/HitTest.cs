// Hit-test da borracha por stroke: Bounds primeiro (barato), depois distância
// ponto→segmento (exato). Tudo em px globais. Tolerância mínima de 6 px para
// strokes finos serem apagáveis com gesto rápido sem exigir precisão cirúrgica.

namespace EpicPencil.Core;

public static class Geometry
{
    public static float DistancePointToSegment(Pt p, Pt a, Pt b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 == 0) return p.DistanceTo(a);
        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0f, 1f);
        float cx = a.X + t * dx, cy = a.Y + t * dy;
        float ex = p.X - cx, ey = p.Y - cy;
        return MathF.Sqrt(ex * ex + ey * ey);
    }
}

public static class HitTest
{
    public static float EraseTolerance(Stroke s) => Math.Max(6f, s.WidthPx / 2);

    public static bool Hits(Stroke s, Pt p)
    {
        // Bounds já inclui pad de width/2+2; checagem rápida antes do loop exato.
        if (!s.Bounds.Contains(p))
        {
            // Permite apagar pela borda: expande pela tolerância total.
            var expanded = s.Bounds.Inflate(EraseTolerance(s));
            if (!expanded.Contains(p)) return false;
        }
        float tol = EraseTolerance(s);
        var pts = s.Points;
        if (pts.Count == 1) return pts[0].DistanceTo(p) <= tol;
        for (int i = 0; i < pts.Count - 1; i++)
            if (Geometry.DistancePointToSegment(p, pts[i], pts[i + 1]) <= tol)
                return true;
        return false;
    }

    // Varredura de trás para frente (z-order): o stroke do topo apaga primeiro.
    public static List<Stroke> PickErase(Document doc, Pt p)
    {
        var hit = new List<Stroke>();
        var all = doc.Strokes;
        for (int i = all.Count - 1; i >= 0; i--)
            if (Hits(all[i], p)) hit.Add(all[i]);
        return hit;
    }

    // Borracha por gesto: testa o SEGMENTO do cursor (não só o ponto), para
    // gesto rápido não "pular" strokes entre dois PointerMoves espaçados.
    public static List<Stroke> PickEraseSegment(Document doc, Pt a, Pt b)
    {
        var hit = new List<Stroke>();
        var all = doc.Strokes;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var s = all[i];
            float tol = EraseTolerance(s);
            var expanded = s.Bounds.Inflate(tol);
            if (!expanded.Contains(a) && !expanded.Contains(b) && !expanded.Intersects(SegmentBounds(a, b)))
                continue;
            if (SegmentHitsStroke(s, a, b, tol)) hit.Add(s);
        }
        return hit;
    }

    // Borracha sobre textos: bounds reais (medidos na fonte/tamanho) com
    // pad de 3 px p/ gesto rápido. Varredura de trás p/ frente (z-order).
    public static List<TextObject> PickEraseTexts(IReadOnlyList<TextObject> texts, Pt a, Pt b)
    {
        var hit = new List<TextObject>();
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var t = texts[i];
            var r = new RectD(t.X - 3, t.Y - 3, t.WidthPx + 6, t.HeightPx + 6);
            if (r.Contains(a) || r.Contains(b) || SegmentCrossesRect(r, a, b))
                hit.Add(t);
        }
        return hit;
    }

    // Borracha sobre formas: CONTORNO, não interior (o interior vazio não
    // apaga — mesma semântica da seleção, REQ6). Testa o SEGMENTO do cursor
    // amostrado, como SegmentHitsStroke: gesto rápido não "pula" a forma.
    // Varredura de trás p/ frente (z-order).
    public static List<RectangleObject> PickEraseRectangles(
        IReadOnlyList<RectangleObject> rects, Pt a, Pt b)
    {
        var hit = new List<RectangleObject>();
        for (int i = rects.Count - 1; i >= 0; i--)
        {
            var r = rects[i];
            float tol = ShapeGeometry.Tolerance(r.StrokeWidthPx);
            var expanded = r.Bounds.Inflate(tol);
            if (!expanded.Contains(a) && !expanded.Contains(b) && !expanded.Intersects(SegmentBounds(a, b)))
                continue;
            if (SegmentHitsRectContour(r, a, b, tol)) hit.Add(r);
        }
        return hit;
    }

    public static List<CircleObject> PickEraseCircles(
        IReadOnlyList<CircleObject> circles, Pt a, Pt b)
    {
        var hit = new List<CircleObject>();
        for (int i = circles.Count - 1; i >= 0; i--)
        {
            var c = circles[i];
            float tol = ShapeGeometry.Tolerance(c.StrokeWidthPx);
            var expanded = c.Bounds.Inflate(tol);
            if (!expanded.Contains(a) && !expanded.Contains(b) && !expanded.Intersects(SegmentBounds(a, b)))
                continue;
            if (SegmentHitsEllipseContour(c, a, b, tol)) hit.Add(c);
        }
        return hit;
    }

    private static bool SegmentHitsRectContour(RectangleObject r, Pt a, Pt b, float tol)
    {
        float len = a.DistanceTo(b);
        int steps = Math.Clamp((int)(len / Math.Max(tol / 2, 1f)), 1, 32);
        for (int k = 0; k <= steps; k++)
        {
            var p = new Pt(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps);
            if (ShapeGeometry.HitRectContour(r.Bounds, r.StrokeWidthPx, p)) return true;
        }
        return false;
    }

    private static bool SegmentHitsEllipseContour(CircleObject c, Pt a, Pt b, float tol)
    {
        float len = a.DistanceTo(b);
        int steps = Math.Clamp((int)(len / Math.Max(tol / 2, 1f)), 1, 32);
        for (int k = 0; k <= steps; k++)
        {
            var p = new Pt(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps);
            if (ShapeGeometry.HitEllipseContour(c.Bounds, c.StrokeWidthPx, p)) return true;
        }
        return false;
    }

    private static bool SegmentCrossesRect(RectD r, Pt a, Pt b)
    {
        float len = a.DistanceTo(b);
        int steps = Math.Clamp((int)(len / 4f), 1, 32);
        for (int k = 1; k < steps; k++)
            if (r.Contains(new Pt(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps)))
                return true;
        return false;
    }

    private static RectD SegmentBounds(Pt a, Pt b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private static bool SegmentHitsStroke(Stroke s, Pt a, Pt b, float tol)
    {
        var pts = s.Points;
        if (pts.Count == 1)
            return Geometry.DistancePointToSegment(pts[0], a, b) <= tol;
        // Amostragem do segmento do cursor em passos de tol/2: barato e suficiente.
        float len = a.DistanceTo(b);
        int steps = Math.Clamp((int)(len / Math.Max(tol / 2, 1f)), 1, 32);
        for (int k = 0; k <= steps; k++)
        {
            var p = new Pt(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps);
            if (Hits(s, p)) return true;
        }
        return false;
    }
}
