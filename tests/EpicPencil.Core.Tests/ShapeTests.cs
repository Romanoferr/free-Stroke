using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class ShapeTests
{
    private static RectangleObject MakeRect(Document doc, float x, float y,
        float w = 120, float h = 80, float widthPx = 4f)
    {
        var r = new RectangleObject(doc.NextId(), x, y, w, h, new Rgba(255, 0, 0), widthPx);
        new UndoStack().Execute(new AddRectangleCommand(r), doc);
        return r;
    }

    private static CircleObject MakeCircle(Document doc, float x, float y,
        float w = 120, float h = 80, float widthPx = 4f)
    {
        var c = new CircleObject(doc.NextId(), x, y, w, h, new Rgba(0, 120, 215), widthPx);
        new UndoStack().Execute(new AddCircleCommand(c), doc);
        return c;
    }

    [Fact]
    public void Normalize_Works_In_All_Four_Drag_Directions()
    {
        Assert.Equal(new RectD(10, 20, 100, 60), ShapeGeometry.Normalize(new Pt(10, 20), new Pt(110, 80)));
        Assert.Equal(new RectD(10, 20, 100, 60), ShapeGeometry.Normalize(new Pt(110, 80), new Pt(10, 20)));
        Assert.Equal(new RectD(10, 20, 100, 60), ShapeGeometry.Normalize(new Pt(110, 20), new Pt(10, 80)));
        Assert.Equal(new RectD(10, 20, 100, 60), ShapeGeometry.Normalize(new Pt(10, 80), new Pt(110, 20)));
    }

    [Fact]
    public void MinSize_Rejects_Click_Without_Drag()
    {
        Assert.False(ShapeGeometry.MeetsMinSize(new RectD(10, 10, 0, 0)));
        Assert.False(ShapeGeometry.MeetsMinSize(new RectD(10, 10, 3.9f, 100)));
        Assert.False(ShapeGeometry.MeetsMinSize(new RectD(10, 10, 100, 2)));
        Assert.True(ShapeGeometry.MeetsMinSize(new RectD(10, 10, 4, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RectangleObject(1, 0, 0, 0, 10, new Rgba(0, 0, 0), 4f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CircleObject(1, 0, 0, 10, 0, new Rgba(0, 0, 0), 4f));
    }

    [Fact]
    public void Rectangle_HitTest_Contour_Only()
    {
        var doc = new Document();
        var r = MakeRect(doc, 100, 100);
        // Sobre cada borda: hit.
        Assert.True(r.HitTest(new Pt(160, 100))); // topo
        Assert.True(r.HitTest(new Pt(160, 180))); // base
        Assert.True(r.HitTest(new Pt(100, 140))); // esquerda
        Assert.True(r.HitTest(new Pt(220, 140))); // direita
        // Interior vazio (longe do contorno): miss.
        Assert.False(r.HitTest(new Pt(160, 140)));
        // Fora: miss.
        Assert.False(r.HitTest(new Pt(50, 50)));
        Assert.False(r.HitTest(new Pt(300, 300)));
        // Perto do contorno (dentro da tolerância): hit.
        Assert.True(r.HitTest(new Pt(160, 104)));
        Assert.True(r.HitTest(new Pt(94, 140)));
    }

    [Fact]
    public void Circle_HitTest_Contour_Only()
    {
        var doc = new Document();
        var c = MakeCircle(doc, 100, 100, 120, 80); // elipse rx=60 ry=40, centro (160,140)
        // Pontos cardeais do contorno: hit.
        Assert.True(c.HitTest(new Pt(220, 140))); // direita
        Assert.True(c.HitTest(new Pt(100, 140))); // esquerda
        Assert.True(c.HitTest(new Pt(160, 100))); // topo
        Assert.True(c.HitTest(new Pt(160, 180))); // base
        // Centro (interior vazio): miss.
        Assert.False(c.HitTest(new Pt(160, 140)));
        // Canto do bbox (fora da elipse): miss.
        Assert.False(c.HitTest(new Pt(100, 100)));
        Assert.False(c.HitTest(new Pt(220, 180)));
        // Fora: miss.
        Assert.False(c.HitTest(new Pt(300, 300)));
        // Perto do contorno: hit.
        Assert.True(c.HitTest(new Pt(219, 144)));
    }

    [Fact]
    public void Shapes_Work_With_Negative_Coords()
    {
        var doc = new Document();
        var r = MakeRect(doc, -1920 + 100, 160 + 100);
        Assert.True(r.HitTest(new Pt(-1920 + 100, 160 + 140)));
        Assert.False(r.HitTest(new Pt(100, 140)));
        var c = MakeCircle(doc, -1920 + 300, 160 + 300, 100, 100);
        Assert.True(c.HitTest(new Pt(-1920 + 400, 160 + 350)));
        var pick = ObjectPicker.PickTopmost(doc, new Pt(-1920 + 100, 160 + 100));
        Assert.NotNull(pick);
        Assert.Equal(CanvasObjectKind.Rectangle, pick.Value.Kind);
    }

    [Fact]
    public void Pick_Respects_Id_Order_Across_Types()
    {
        var doc = new Document();
        // Retângulo criado antes, círculo depois e sobreposto: círculo vence.
        var rect = MakeRect(doc, 100, 100, 200, 200);
        var circ = MakeCircle(doc, 100, 100, 200, 200);
        // Ponto sobre AMBOS os contornos (canto do bbox está fora da elipse;
        // usa o meio da borda superior: retângulo y=100, elipse topo y=100).
        var pick = ObjectPicker.PickTopmost(doc, new Pt(200, 100));
        Assert.NotNull(pick);
        Assert.Equal(circ.Id, pick.Value.Id);
        Assert.Equal(CanvasObjectKind.Circle, pick.Value.Kind);
        _ = rect;
    }

    [Fact]
    public void Pick_Rectangle_Over_Circle_When_Newer()
    {
        var doc = new Document();
        var circ = MakeCircle(doc, 100, 100, 200, 200);
        var rect = MakeRect(doc, 100, 100, 200, 200);
        var pick = ObjectPicker.PickTopmost(doc, new Pt(200, 100));
        Assert.NotNull(pick);
        Assert.Equal(rect.Id, pick.Value.Id);
        _ = circ;
    }

    [Fact]
    public void Pick_Text_Beats_Shape_When_Newer_And_Vice_Versa()
    {
        var doc = new Document();
        var rect = MakeRect(doc, 100, 100, 200, 60);
        var t = new TextObject(doc.NextId(), 100, 95, "T", 20f, "Space Mono",
            new Rgba(0, 0, 0), 200, 30);
        new UndoStack().Execute(new AddTextCommand(t), doc);
        // Texto (mais novo) cobre a borda superior do retângulo.
        var pick = ObjectPicker.PickTopmost(doc, new Pt(150, 100));
        Assert.NotNull(pick);
        Assert.Equal(CanvasObjectKind.Text, pick.Value.Kind);
        _ = rect;
    }

    [Fact]
    public void Pick_Shape_Beats_Screen()
    {
        var doc = new Document();
        var s = new ScreenObject(doc.NextId(), 90, 90, 220, 220, 8, 6, new byte[4 * 8 * 6]);
        new UndoStack().Execute(new AddScreenCommand(s), doc);
        var rect = MakeRect(doc, 100, 100);
        var pick = ObjectPicker.PickTopmost(doc, new Pt(100, 140));
        Assert.NotNull(pick);
        Assert.Equal(rect.Id, pick.Value.Id);
    }

    [Fact]
    public void Pick_Interior_Empty_Selects_Nothing()
    {
        var doc = new Document();
        MakeRect(doc, 100, 100, 200, 200);
        MakeCircle(doc, 500, 500, 200, 200);
        Assert.Null(ObjectPicker.PickTopmost(doc, new Pt(200, 200))); // interior do ret
        Assert.Null(ObjectPicker.PickTopmost(doc, new Pt(600, 600))); // centro do círculo
    }

    [Fact]
    public void Move_Add_Delete_Undo_Via_Commands()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var r = new RectangleObject(doc.NextId(), 100, 100, 120, 80, new Rgba(255, 0, 0), 4f);
        undo.Execute(new AddRectangleCommand(r), doc);
        Assert.Single(doc.Rectangles);
        Assert.True(undo.TryUndo(doc));
        Assert.Empty(doc.Rectangles);
        Assert.True(undo.TryRedo(doc));
        Assert.Single(doc.Rectangles);

        undo.Execute(new MoveShapeCommand(r, 100, 100, 200, 150), doc);
        Assert.Equal(200, r.X);
        Assert.True(undo.TryUndo(doc));
        Assert.Equal(100, r.X);
        Assert.True(undo.TryRedo(doc));
        Assert.Equal(200, r.X);

        undo.Execute(new EraseRectanglesCommand(new[] { r }, doc), doc);
        Assert.Empty(doc.Rectangles);
        Assert.True(undo.TryUndo(doc));
        Assert.Single(doc.Rectangles);

        var c = new CircleObject(doc.NextId(), 10, 10, 50, 50, new Rgba(0, 0, 0), 3f);
        undo.Execute(new AddCircleCommand(c), doc);
        undo.Execute(new MoveShapeCommand(c, 10, 10, 30, 40), doc);
        Assert.Equal(30, c.X);
        Assert.True(undo.TryUndo(doc));
        Assert.Equal(10, c.X);
        undo.Execute(new EraseCirclesCommand(new[] { c }, doc), doc);
        Assert.Empty(doc.Circles);
        Assert.True(undo.TryUndo(doc));
        Assert.Single(doc.Circles);
    }

    [Fact]
    public void Erase_Rectangle_Needs_Contour_Contact()
    {
        var doc = new Document();
        MakeRect(doc, 100, 100, 200, 100);
        // Atravessa a borda superior: apaga.
        Assert.Single(HitTest.PickEraseRectangles(doc.Rectangles, new Pt(50, 50), new Pt(250, 150)));
        // Só interior vazio: não apaga.
        Assert.Empty(HitTest.PickEraseRectangles(doc.Rectangles, new Pt(150, 130), new Pt(250, 170)));
        // Fora: não apaga.
        Assert.Empty(HitTest.PickEraseRectangles(doc.Rectangles, new Pt(0, 0), new Pt(50, 50)));
        // Toque pontual sobre a borda (down sem arrasto): apaga.
        Assert.Single(HitTest.PickEraseRectangles(doc.Rectangles, new Pt(150, 100), new Pt(150, 100)));
    }

    [Fact]
    public void Erase_Circle_Needs_Contour_Contact()
    {
        var doc = new Document();
        MakeCircle(doc, 100, 100, 100, 100); // centro (150,150) r=50
        // Diâmetro horizontal: cruza o contorno 2×.
        Assert.Single(HitTest.PickEraseCircles(doc.Circles, new Pt(100, 150), new Pt(200, 150)));
        // Segmento curto no centro (interior vazio): não apaga.
        Assert.Empty(HitTest.PickEraseCircles(doc.Circles, new Pt(140, 150), new Pt(160, 150)));
        // Fora: não apaga.
        Assert.Empty(HitTest.PickEraseCircles(doc.Circles, new Pt(0, 0), new Pt(50, 50)));
    }

    [Fact]
    public void Erase_Shapes_Undo_Restores()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var r = MakeRect(doc, 100, 100);
        var c = MakeCircle(doc, 400, 400);
        var hitR = HitTest.PickEraseRectangles(doc.Rectangles, new Pt(50, 50), new Pt(250, 150));
        var hitC = HitTest.PickEraseCircles(doc.Circles, new Pt(400, 450), new Pt(500, 450));
        undo.Execute(new CompositeCommand(
            new EraseRectanglesCommand(hitR, doc),
            new EraseCirclesCommand(hitC, doc)), doc);
        Assert.Empty(doc.Rectangles);
        Assert.Empty(doc.Circles);
        Assert.True(undo.TryUndo(doc));
        Assert.Single(doc.Rectangles);
        Assert.Single(doc.Circles);
        Assert.True(undo.TryRedo(doc));
        Assert.Empty(doc.Rectangles);
        Assert.Empty(doc.Circles);
        _ = r; _ = c;
    }
    [Fact]
    public void MoveShapeCommand_Rejects_Non_Shapes()
    {
        var doc = new Document();
        var t = new TextObject(doc.NextId(), 0, 0, "x", 20f, "F", new Rgba(0, 0, 0), 10, 10);
        Assert.Throws<ArgumentException>(() => new MoveShapeCommand(
            (IMovableShape)(object)new ShapeProxy(t.Id), 0, 0, 1, 1));
    }

    private sealed class ShapeProxy : IMovableShape
    {
        public ShapeProxy(int id) => Id = id;
        public int Id { get; }
        public CanvasObjectKind Kind => CanvasObjectKind.Text;
        public RectD Bounds => RectD.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public bool HitTest(Pt p) => false;
    }
}
