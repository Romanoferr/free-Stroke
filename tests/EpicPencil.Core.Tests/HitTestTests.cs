using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class HitTestTests
{
    [Fact]
    public void Fast_Gesture_Segment_Does_Not_Skip_Stroke()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var pts = new List<Pt> { new(100, 100), new(110, 100) };
        var s = new Stroke(doc.NextId(), ToolKind.Pen, new Rgba(0, 0, 0), 4f, 1f, pts);
        undo.Execute(new AddStrokeCommand(s), doc);
        // Cursor pula de x=0 para x=200 em um único move: teste por ponto falharia.
        Assert.Empty(HitTest.PickErase(doc, new Pt(0, 100)));
        Assert.Empty(HitTest.PickErase(doc, new Pt(200, 100)));
        Assert.Single(HitTest.PickEraseSegment(doc, new Pt(0, 100), new Pt(200, 100)));
    }

    [Fact]
    public void Thin_Stroke_Has_Minimum_Erase_Tolerance()
    {
        var s = new Stroke(1, ToolKind.Pen, new Rgba(0, 0, 0), 1f, 1f,
            new List<Pt> { new(0, 0), new(100, 0) });
        Assert.True(HitTest.Hits(s, new Pt(50, 5))); // 5 DIP de distância apaga traço de 1px
        Assert.False(HitTest.Hits(s, new Pt(50, 50)));
    }
}
