// Captura incremental: descarta micro-jitter por distância mínima.
// RDP (Douglas-Peucker) roda UMA vez no commit — nunca a cada PointerMove
// (O(n²) por move mataria a latência; decisão §3.2 do Review).

namespace EpicPencil.Core;

public static class InputFilter
{
    public static bool TryAppend(List<Pt> points, Pt candidate, float minDistanceDip)
    {
        if (points.Count == 0)
        {
            points.Add(candidate);
            return true;
        }
        if (points[^1].DistanceTo(candidate) >= minDistanceDip)
        {
            points.Add(candidate);
            return true;
        }
        return false;
    }
}

public static class Rdp
{
    public static List<Pt> Simplify(IReadOnlyList<Pt> points, float toleranceDip)
    {
        if (points.Count < 3 || toleranceDip <= 0) return new List<Pt>(points);
        float tol2 = toleranceDip * toleranceDip;
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        SimplifySection(points, 0, points.Count - 1, tol2, keep);
        var result = new List<Pt>(points.Count);
        for (int i = 0; i < points.Count; i++)
            if (keep[i]) result.Add(points[i]);
        return result;
    }

    private static void SimplifySection(IReadOnlyList<Pt> pts, int first, int last, float tol2, bool[] keep)
    {
        float maxDist2 = 0;
        int index = -1;
        var a = pts[first];
        var b = pts[last];
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        for (int i = first + 1; i < last; i++)
        {
            float d2 = len2 == 0
                ? Dist2(pts[i], a)
                : DistToSegment2(pts[i], a, dx, dy, len2);
            if (d2 > maxDist2)
            {
                maxDist2 = d2;
                index = i;
            }
        }
        if (index >= 0 && maxDist2 > tol2)
        {
            keep[index] = true;
            SimplifySection(pts, first, index, tol2, keep);
            SimplifySection(pts, index, last, tol2, keep);
        }
    }

    private static float Dist2(Pt p, Pt a)
    {
        float dx = p.X - a.X, dy = p.Y - a.Y;
        return dx * dx + dy * dy;
    }

    private static float DistToSegment2(Pt p, Pt a, float dx, float dy, float len2)
    {
        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0f, 1f);
        float cx = a.X + t * dx, cy = a.Y + t * dy;
        float ex = p.X - cx, ey = p.Y - cy;
        return ex * ex + ey * ey;
    }
}
