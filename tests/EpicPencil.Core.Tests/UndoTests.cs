using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class UndoTests
{
    private static Stroke Mk(Document doc, int n = 5)
    {
        var pts = new List<Pt>();
        for (int i = 0; i < n; i++) pts.Add(new Pt(i * 10, i * 10));
        return new Stroke(doc.NextId(), ToolKind.Pen, new Rgba(255, 0, 0), 4f, 1f, pts);
    }

    [Fact]
    public void Add_Undo_Redo_Roundtrip()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var s = Mk(doc);
        undo.Execute(new AddStrokeCommand(s), doc);
        Assert.Single(doc.Strokes);
        Assert.True(undo.TryUndo(doc));
        Assert.Empty(doc.Strokes);
        Assert.True(undo.TryRedo(doc));
        Assert.Single(doc.Strokes);
    }

    [Fact]
    public void Erase_Restores_ZOrder()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var a = Mk(doc); var b = Mk(doc); var c = Mk(doc);
        undo.Execute(new AddStrokeCommand(a), doc);
        undo.Execute(new AddStrokeCommand(b), doc);
        undo.Execute(new AddStrokeCommand(c), doc);
        undo.Execute(new EraseStrokesCommand(new[] { b }, doc), doc);
        Assert.Equal(new[] { a, c }, doc.Strokes.ToArray());
        undo.TryUndo(doc);
        Assert.Equal(new[] { a, b, c }, doc.Strokes.ToArray());
    }

    [Fact]
    public void Clear_Undo_Restores_Everything()
    {
        var doc = new Document();
        var undo = new UndoStack();
        for (int i = 0; i < 10; i++) undo.Execute(new AddStrokeCommand(Mk(doc, 20)), doc);
        undo.Execute(new ClearAllCommand(doc), doc);
        Assert.Empty(doc.Strokes);
        undo.TryUndo(doc);
        Assert.Equal(10, doc.Count);
    }

    [Fact]
    public void Caps_Evict_Oldest_And_Bound_Memory()
    {
        var doc = new Document();
        var undo = new UndoStack();
        for (int i = 0; i < 70; i++) undo.Execute(new AddStrokeCommand(Mk(doc, 10)), doc);
        Assert.True(undo.UndoCount <= UndoStack.MaxCommands);
        int undone = 0;
        while (undo.TryUndo(doc)) undone++;
        Assert.Equal(undo.UndoCount + undone, Math.Min(70, UndoStack.MaxCommands));
    }
}
