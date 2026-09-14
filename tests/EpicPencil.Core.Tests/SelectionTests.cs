using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class SelectionTests
{
    private static TextObject MakeText(Document doc, float x, float y, float w = 60, float h = 20)
    {
        var t = new TextObject(doc.NextId(), x, y, "T", 20f, "Space Mono", new Rgba(0, 0, 0), w, h);
        new UndoStack().Execute(new AddTextCommand(t), doc);
        return t;
    }

    private static ScreenObject MakeScreen(Document doc, float x, float y, float w = 100, float h = 80)
    {
        var s = new ScreenObject(doc.NextId(), x, y, w, h, 8, 6, new byte[4 * 8 * 6]);
        new UndoStack().Execute(new AddScreenCommand(s), doc);
        return s;
    }

    [Fact]
    public void Pick_Prefers_Text_Over_Screen_Overlap()
    {
        var doc = new Document();
        var screen = MakeScreen(doc, 100, 100);
        var text = MakeText(doc, 110, 110);
        var pick = ObjectPicker.PickTopmost(doc, new Pt(120, 120));
        Assert.NotNull(pick);
        Assert.Equal(CanvasObjectKind.Text, pick.Value.Kind);
        Assert.Equal(text.Id, pick.Value.Id);
        _ = screen;
    }

    [Fact]
    public void Pick_Topmost_Within_Same_Type_Wins()
    {
        var doc = new Document();
        var a = MakeText(doc, 100, 100);
        var b = MakeText(doc, 110, 110);
        var pick = ObjectPicker.PickTopmost(doc, new Pt(115, 115));
        Assert.NotNull(pick);
        Assert.Equal(b.Id, pick.Value.Id);
        _ = a;
    }

    [Fact]
    public void Pick_Empty_Returns_Null()
    {
        var doc = new Document();
        MakeText(doc, 100, 100);
        Assert.Null(ObjectPicker.PickTopmost(doc, new Pt(1500, 800)));
    }

    [Fact]
    public void Pick_Respects_Position_And_Negative_Coords()
    {
        var doc = new Document();
        var t = MakeText(doc, -1920 + 100, 160 + 100);
        Assert.NotNull(ObjectPicker.PickTopmost(doc, new Pt(-1920 + 105, 160 + 105)));
        Assert.Null(ObjectPicker.PickTopmost(doc, new Pt(105, 105)));
        _ = t;
    }

    [Fact]
    public void Pick_Text_Pad_Allows_Near_Miss()
    {
        var doc = new Document();
        MakeText(doc, 100, 100, 10, 10);
        // 2px fora do bounds ainda atinge (pad 3px); 10px fora não.
        Assert.NotNull(ObjectPicker.PickTopmost(doc, new Pt(98, 98)));
        Assert.Null(ObjectPicker.PickTopmost(doc, new Pt(80, 80)));
    }

    [Fact]
    public void Move_Delete_Undo_Via_Commands()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var t = new TextObject(doc.NextId(), 100, 100, "abc", 20f, "Space Mono", new Rgba(0, 0, 0), 60, 20);
        undo.Execute(new AddTextCommand(t), doc);
        undo.Execute(new MoveTextCommand(t, 100, 100, 200, 150), doc);
        Assert.Equal(200, t.X);
        Assert.True(undo.TryUndo(doc));
        Assert.Equal(100, t.X);
        Assert.True(undo.TryRedo(doc));
        Assert.Equal(200, t.X);

        undo.Execute(new EraseTextsCommand(new[] { t }, doc), doc);
        Assert.Empty(doc.Texts);
        Assert.True(undo.TryUndo(doc));
        Assert.Single(doc.Texts);
        Assert.True(undo.TryRedo(doc));
        Assert.Empty(doc.Texts);
    }

    [Fact]
    public void CanvasObjects_Expose_Bounds_And_HitTest()
    {
        var doc = new Document();
        ICanvasObject text = MakeText(doc, 50, 60, 60, 20);
        ICanvasObject screen = MakeScreen(doc, 300, 300);
        Assert.Equal(CanvasObjectKind.Text, text.Kind);
        Assert.Equal(CanvasObjectKind.Screen, screen.Kind);
        Assert.True(text.HitTest(new Pt(55, 65)));
        Assert.False(text.HitTest(new Pt(500, 500)));
        Assert.True(screen.HitTest(new Pt(305, 305)));
        Assert.False(screen.HitTest(new Pt(0, 0)));
    }
}
