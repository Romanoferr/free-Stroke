using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class ScreenUndoTests
{
    private static ScreenObject Mk(Document doc, float x = 10, float y = 20)
    {
        var bgra = new byte[4 * 8 * 6];
        return new ScreenObject(doc.NextId(), x, y, 8f, 6f, 8, 6, bgra);
    }

    [Fact]
    public void Add_Move_Delete_Undo_Chain()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var s = Mk(doc);
        undo.Execute(new AddScreenCommand(s), doc);
        Assert.Single(doc.Screens);

        undo.Execute(new MoveScreenCommand(s, 10, 20, 100, 200), doc);
        Assert.Equal(100, s.X);
        Assert.Equal(200, s.Y);
        Assert.True(undo.TryUndo(doc));
        Assert.Equal(10, s.X); // move desfeito volta à origem
        Assert.True(undo.TryRedo(doc));
        Assert.Equal(100, s.X);

        undo.Execute(new RemoveScreenCommand(new[] { s }, doc), doc);
        Assert.Empty(doc.Screens);
        Assert.True(undo.TryUndo(doc));
        Assert.Single(doc.Screens);
    }

    [Fact]
    public void Stroke_Commands_Expose_Empty_Screens()
    {
        var doc = new Document();
        var pts = new List<Pt> { new(0, 0) };
        IDocCommand cmd = new AddStrokeCommand(new Stroke(doc.NextId(), ToolKind.Pen, new Rgba(0, 0, 0), 2f, 1f, pts));
        Assert.Empty(cmd.AffectedScreens); // default member: zero churn nos comandos antigos
    }

    [Fact]
    public void Screen_Validation_Rejects_Bad_Bytes()
    {
        var doc = new Document();
        Assert.Throws<ArgumentException>(() =>
            new ScreenObject(doc.NextId(), 0, 0, 8f, 6f, 8, 6, new byte[10]));
    }
}
