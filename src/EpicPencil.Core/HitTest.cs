// Hit-test da borracha por stroke: Bounds primeiro (barato), depois distância
// ponto→segmento (exato). Tolerância mínima de 6 DIP para strokes finos serem
// apagáveis com gesto rápido sem exigir precisão cirúrgica.

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
    public static float EraseTolerance(Stroke s) => Math.Max(6f, s.WidthDip / 2);

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
