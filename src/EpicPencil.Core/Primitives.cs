// Primitivas geométricas em DIP lógicos (independentes de DPI/monitor).
// Decisão não óbvia: pontos são só X/Y. Sem timestamp/pressão por ponto no MVP
// (dobra o tamanho do stroke sem uso); pressão entra como campo futuro sem migração.

namespace EpicPencil.Core;

public readonly record struct Pt(float X, float Y)
{
    public float DistanceTo(in Pt other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

public readonly record struct RectD(float X, float Y, float Width, float Height)
{
    public static readonly RectD Empty = new(0, 0, 0, 0);

    public float Right => X + Width;
    public float Bottom => Y + Height;

    public RectD Inflate(float amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public bool Intersects(in RectD other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public bool Contains(in Pt p) =>
        p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}
