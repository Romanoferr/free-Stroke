// Conversão de coordenadas multi-monitor (matemática pura, sem HWND).
// Casos do layout real: primário (0,0), direita (+X), esquerda (-X), acima (-Y).

using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class MonitorSpaceTests
{
    private static readonly MonitorFrame Primary = new(0, 0, 1, 1);
    private static readonly MonitorFrame Right = new(1920, 0, 1, 1);
    private static readonly MonitorFrame Left = new(-1920, 0, 1, 1);
    private static readonly MonitorFrame Above = new(0, -1080, 1, 1);

    [Fact]
    public void IdentityKeepsCoordinates()
    {
        var p = new Pt(960, 540);
        Assert.Equal(p, MonitorFrame.Identity.ToGlobal(p));
        Assert.Equal(p, MonitorFrame.Identity.ToLocal(p));
    }

    [Fact]
    public void ScreenClickConvertsToSecondaryLocal()
    {
        // Clique global X=-1500 no monitor à esquerda → local 420.
        var local = Left.ToLocal(new Pt(-1500, 300));
        Assert.Equal(420, local.X, precision: 3);
        Assert.Equal(300, local.Y, precision: 3);
    }

    [Fact]
    public void CornersAndCenterOnAllSides()
    {
        // Primário 1920x1080 em (0,0).
        Assert.Equal(new Pt(0, 0), Primary.ToLocal(new Pt(0, 0))); // top-left
        Assert.Equal(new Pt(1919, 0), Primary.ToLocal(new Pt(1919, 0))); // top-right
        Assert.Equal(new Pt(0, 1079), Primary.ToLocal(new Pt(0, 1079))); // bottom-left
        Assert.Equal(new Pt(960, 540), Primary.ToLocal(new Pt(960, 540))); // centro
        // Direita: global (1920,0) = local (0,0); centro (2880,540) = (960,540).
        Assert.Equal(new Pt(0, 0), Right.ToLocal(new Pt(1920, 0)));
        Assert.Equal(new Pt(960, 540), Right.ToLocal(new Pt(2880, 540)));
        Assert.Equal(new Pt(1919, 1079), Right.ToLocal(new Pt(3839, 1079)));
        // Esquerda: global (-1920,0) = local (0,0); (-1,1079) = (1919,1079).
        Assert.Equal(new Pt(0, 0), Left.ToLocal(new Pt(-1920, 0)));
        Assert.Equal(new Pt(1919, 1079), Left.ToLocal(new Pt(-1, 1079)));
        // Acima: global (0,-1080) = local (0,0); (960,-540) = centro.
        Assert.Equal(new Pt(0, 0), Above.ToLocal(new Pt(0, -1080)));
        Assert.Equal(new Pt(960, 540), Above.ToLocal(new Pt(960, -540)));
    }

    [Fact]
    public void RoundTripIsIdentity()
    {
        var frames = new[] { Primary, Right, Left, Above, new MonitorFrame(-1920, 160, 1.25f, 1.25f) };
        var points = new[] { new Pt(0, 0), new Pt(1919, 1079), new Pt(960, 540), new Pt(13.5f, 777.25f) };
        foreach (var f in frames)
            foreach (var p in points)
            {
                var back = f.ToLocal(f.ToGlobal(p));
                Assert.Equal(p.X, back.X, precision: 3);
                Assert.Equal(p.Y, back.Y, precision: 3);
            }
    }

    [Fact]
    public void RectConversionPreservesSizeAtScale1()
    {
        var local = new RectD(100, 100, 200, 150);
        var global = Left.ToGlobal(local);
        Assert.Equal(-1820, global.X, precision: 3);
        Assert.Equal(100, global.Y, precision: 3);
        Assert.Equal(200, global.Width, precision: 3);
        Assert.Equal(150, global.Height, precision: 3);
        var back = Left.ToLocal(global);
        Assert.Equal(local.X, back.X, precision: 3);
        Assert.Equal(local.Width, back.Width, precision: 3);
    }

    [Fact]
    public void FractionalScaleConvertsBothWays()
    {
        // Monitor 150%: 100 DIP locais = 150 px globais.
        var f = new MonitorFrame(1920, 0, 1.5f, 1.5f);
        var g = f.ToGlobal(new Pt(100, 100));
        Assert.Equal(2070, g.X, precision: 3);
        Assert.Equal(150, g.Y, precision: 3);
        var l = f.ToLocal(g);
        Assert.Equal(100, l.X, precision: 3);
        Assert.Equal(100, l.Y, precision: 3);
    }

    [Fact]
    public void ContainsGlobalRespectsNegativeOrigin()
    {
        Assert.True(Left.ContainsGlobal(new Pt(-1920, 0), 1920, 1080));
        Assert.True(Left.ContainsGlobal(new Pt(-1, 1079), 1920, 1080));
        Assert.False(Left.ContainsGlobal(new Pt(0, 0), 1920, 1080)); // borda = primário
        Assert.False(Left.ContainsGlobal(new Pt(-1921, 500), 1920, 1080));
    }

    [Fact]
    public void ListConversionShiftsEveryPoint()
    {
        var pts = new List<Pt> { new(0, 0), new(100, 50) };
        var global = Left.ToGlobalList(pts);
        Assert.Equal(-1920, global[0].X, precision: 3);
        Assert.Equal(-1820, global[1].X, precision: 3);
        Assert.Equal(50, global[1].Y, precision: 3);
        Assert.Equal(2, global.Count);
    }
}
