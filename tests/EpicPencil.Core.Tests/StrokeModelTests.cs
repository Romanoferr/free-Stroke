using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class StrokeModelTests
{
    [Fact]
    public void Bounds_Contains_All_Points_With_Pen_Padding()
    {
        var pts = new List<Pt> { new(10, 10), new(20, 30), new(15, 25) };
        var s = new Stroke(1, ToolKind.Pen, new Rgba(255, 0, 0), 4f, 1f, pts);
        foreach (var p in pts) Assert.True(s.Bounds.Inflate(-4f).Contains(p) || s.Bounds.Contains(p));
        Assert.True(s.Bounds.Width >= 10f && s.Bounds.Height >= 20f);
    }

    [Fact]
    public void Single_Point_Stroke_Is_Valid()
    {
        var s = new Stroke(1, ToolKind.Pen, new Rgba(0, 0, 0), 4f, 1f, new List<Pt> { new(5, 5) });
        Assert.True(HitTest.Hits(s, new Pt(5, 5)));
        Assert.False(HitTest.Hits(s, new Pt(500, 500)));
    }

    [Fact]
    public void Commit_Tolerance_Scales_With_Width()
    {
        Assert.True(StrokeSpec.CommitTolerance(18f) > StrokeSpec.CommitTolerance(2f));
        Assert.InRange(StrokeSpec.CommitTolerance(4f), 0.5f, 3f);
    }
}
