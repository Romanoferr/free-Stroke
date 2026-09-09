using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class RdpTests
{
    [Fact]
    public void Straight_Line_Collapses_To_Endpoints()
    {
        var pts = new List<Pt>();
        for (int i = 0; i <= 100; i++) pts.Add(new Pt(i, 0));
        var out_ = Rdp.Simplify(pts, 1f);
        Assert.Equal(2, out_.Count);
    }

    [Fact]
    public void Right_Angle_Preserves_Corner()
    {
        var pts = new List<Pt> { new(0, 0), new(50, 0), new(100, 0), new(100, 50), new(100, 100) };
        var out_ = Rdp.Simplify(pts, 1f);
        Assert.Contains(out_, p => Math.Abs(p.X - 100) < 0.01 && Math.Abs(p.Y) < 0.01);
        Assert.True(out_.Count >= 3);
    }

    [Fact]
    public void Circle_Keeps_Shape_With_Moderate_Tolerance()
    {
        var pts = new List<Pt>();
        for (int i = 0; i < 360; i += 2)
        {
            float a = i * MathF.PI / 180;
            pts.Add(new Pt(100 + 50 * MathF.Cos(a), 100 + 50 * MathF.Sin(a)));
        }
        var out_ = Rdp.Simplify(pts, 1.5f);
        Assert.True(out_.Count < pts.Count);
        Assert.True(out_.Count > 8); // círculo não pode virar triângulo
        var bounds = Stroke.ComputeBounds(out_, 4f);
        Assert.True(bounds.Width > 80 && bounds.Height > 80);
    }
}
