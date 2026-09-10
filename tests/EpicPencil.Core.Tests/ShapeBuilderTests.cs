using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class ShapeBuilderTests
{
    [Fact]
    public void Line_Is_Just_Two_Points()
    {
        var pts = ShapeBuilder.BuildLine(new Pt(0, 0), new Pt(100, 50));
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void Arrow_Has_Tip_And_Two_Wings_At_Head_Length()
    {
        var a = new Pt(0, 0);
        var b = new Pt(100, 0);
        var pts = ShapeBuilder.BuildArrow(a, b, 5f);
        Assert.Equal(5, pts.Count);
        Assert.Equal(a, pts[0]);
        Assert.Equal(b, pts[1]);
        Assert.Equal(b, pts[3]); // fuste revisitado entre as asas
        float headLen = Math.Clamp(5f * 3.5f, 12f, 40f);
        Assert.InRange(pts[2].DistanceTo(b), headLen - 0.01f, headLen + 0.01f);
        Assert.InRange(pts[4].DistanceTo(b), headLen - 0.01f, headLen + 0.01f);
        // Asas atrás da ponta (x menor que a ponta, indo para a esquerda).
        Assert.True(pts[2].X < b.X && pts[4].X < b.X);
        // Simetria vertical para seta horizontal.
        Assert.Equal(pts[2].Y, -pts[4].Y, 2);
    }

    [Fact]
    public void Arrow_Zero_Length_Degrades_To_Dot()
    {
        var pts = ShapeBuilder.BuildArrow(new Pt(10, 10), new Pt(10.5f, 10), 5f);
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void Arrow_Is_Erasable_By_Head()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var pts = ShapeBuilder.BuildArrow(new Pt(0, 0), new Pt(100, 0), 5f);
        var s = new Stroke(doc.NextId(), ToolKind.Arrow, new Rgba(255, 0, 0), 5f, 1f, pts);
        undo.Execute(new AddStrokeCommand(s), doc);
        // Tocar a asa apaga a seta inteira (geometria assada => hit-test unificado).
        var wing = pts[2];
        Assert.Single(HitTest.PickErase(doc, wing));
    }

    [Fact]
    public void WidthPreset_Orders_S_M_L()
    {
        foreach (var tool in new[] { ToolKind.Pen, ToolKind.Highlighter, ToolKind.Line, ToolKind.Arrow })
        {
            float s = StrokeSpec.WidthPreset(tool, 0);
            float m = StrokeSpec.WidthPreset(tool, 1);
            float l = StrokeSpec.WidthPreset(tool, 2);
            Assert.True(s < m && m < l);
            Assert.Equal(StrokeSpec.DefaultWidth(tool), m);
        }
    }
}
