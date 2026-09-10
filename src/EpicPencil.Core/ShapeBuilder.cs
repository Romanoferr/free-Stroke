// Geometria de formas commitada como POLILINHA assada (baked), não como
// entidade paramétrica. Motivo: render interativo, hit-test da borracha,
// RDP e export futuro percorrem o mesmo caminho de pontos — zero path especial.
// A seta [base, ponta, asaA, ponta, asaB] sobrepõe o fuste perto da ponta,
// invisível porque formas são sempre opacas (ver AppState.AddShape).

namespace EpicPencil.Core;

public static class ShapeBuilder
{
    private const float HeadAngleRad = 28f * MathF.PI / 180f;

    public static List<Pt> BuildLine(Pt a, Pt b) => new() { a, b };

    public static List<Pt> BuildArrow(Pt a, Pt b, float widthDip)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 2f) return new() { a, b }; // clique sem arrasto: ponto mínimo

        float headLen = Math.Clamp(widthDip * 3.5f, 12f, 40f);
        // Asas: vetor reverso unitário (-u) rotacionado por ±28° a partir da ponta.
        float ux = dx / len, uy = dy / len;
        var wingA = Rot(b, -ux, -uy, HeadAngleRad, headLen);
        var wingB = Rot(b, -ux, -uy, -HeadAngleRad, headLen);
        return new() { a, b, wingA, b, wingB };
    }

    private static Pt Rot(Pt origin, float vx, float vy, float angle, float scale)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return new Pt(
            origin.X + (vx * cos - vy * sin) * scale,
            origin.Y + (vx * sin + vy * cos) * scale);
    }
}
