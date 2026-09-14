// Comandos das formas geométricas: mesmo padrão dos demais tipos
// (Add/Erase/Move), no UndoStack existente — sem segundo sistema (REQ9).
// Add/Erase seguem o molde de texto/captura (restaura z-order por índice).
// Move é GENÉRICO (MoveShapeCommand sobre IMovableShape): um único comando
// cobre Rectangle e Circle, sem MoveCircle/MoveRectangle duplicados (REQ8).

using EpicPencil.Core;

namespace EpicPencil.Core;

public sealed class AddRectangleCommand : IDocCommand
{
    private readonly RectangleObject _rect;
    public AddRectangleCommand(RectangleObject rect) => _rect = rect;
    public int RetainedPoints => 0; // objeto segue no Document; nada retido a mais
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<RectangleObject> AffectedRectangles => [_rect];
    public void Do(Document doc) => doc.AddRectangle(_rect);
    public void Undo(Document doc) => doc.RemoveRectangle(_rect);
}

public sealed class AddCircleCommand : IDocCommand
{
    private readonly CircleObject _circle;
    public AddCircleCommand(CircleObject circle) => _circle = circle;
    public int RetainedPoints => 0;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<CircleObject> AffectedCircles => [_circle];
    public void Do(Document doc) => doc.AddCircle(_circle);
    public void Undo(Document doc) => doc.RemoveCircle(_circle);
}

public sealed class EraseRectanglesCommand : IDocCommand
{
    private readonly List<(RectangleObject Rect, int Index)> _removed = new();
    public EraseRectanglesCommand(IReadOnlyList<RectangleObject> removed, Document doc)
    {
        var all = doc.Rectangles;
        foreach (var r in removed)
            _removed.Add((r, IndexOf(all, r)));
    }
    public int RetainedPoints => 0;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<RectangleObject> AffectedRectangles => _removed.Select(r => r.Rect).ToList();
    public void Do(Document doc)
    {
        foreach (var r in _removed) doc.RemoveRectangle(r.Rect);
    }
    public void Undo(Document doc)
    {
        var ordered = new List<(RectangleObject Rect, int Index)>(_removed);
        ordered.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var r in ordered)
            doc.InsertRectangle(r.Rect, Math.Min(r.Index, doc.RectangleCount));
    }
    private static int IndexOf(IReadOnlyList<RectangleObject> all, RectangleObject r)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], r)) return i;
        return all.Count;
    }
}

public sealed class EraseCirclesCommand : IDocCommand
{
    private readonly List<(CircleObject Circle, int Index)> _removed = new();
    public EraseCirclesCommand(IReadOnlyList<CircleObject> removed, Document doc)
    {
        var all = doc.Circles;
        foreach (var c in removed)
            _removed.Add((c, IndexOf(all, c)));
    }
    public int RetainedPoints => 0;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<CircleObject> AffectedCircles => _removed.Select(r => r.Circle).ToList();
    public void Do(Document doc)
    {
        foreach (var r in _removed) doc.RemoveCircle(r.Circle);
    }
    public void Undo(Document doc)
    {
        var ordered = new List<(CircleObject Circle, int Index)>(_removed);
        ordered.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var r in ordered)
            doc.InsertCircle(r.Circle, Math.Min(r.Index, doc.CircleCount));
    }
    private static int IndexOf(IReadOnlyList<CircleObject> all, CircleObject c)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], c)) return i;
        return all.Count;
    }
}

// Movimento genérico das formas: o arrasto ao vivo move só o VISUAL
// (Offset); o modelo commita no drop — sem spam de comandos.
public sealed class MoveShapeCommand : IDocCommand
{
    private readonly IMovableShape _target;
    private readonly float _oldX, _oldY, _newX, _newY;
    public MoveShapeCommand(IMovableShape target, float oldX, float oldY, float newX, float newY)
    {
        if (target.Kind is not (CanvasObjectKind.Rectangle or CanvasObjectKind.Circle))
            throw new ArgumentException("MoveShapeCommand cobre só Rectangle/Circle", nameof(target));
        _target = target;
        _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }
    public CanvasObjectKind Kind => _target.Kind;
    public int TargetId => _target.Id;
    public int RetainedPoints => 0;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<RectangleObject> AffectedRectangles =>
        _target is RectangleObject r ? [r] : Array.Empty<RectangleObject>();
    public IReadOnlyList<CircleObject> AffectedCircles =>
        _target is CircleObject c ? [c] : Array.Empty<CircleObject>();
    public void Do(Document doc) { _target.X = _newX; _target.Y = _newY; }
    public void Undo(Document doc) { _target.X = _oldX; _target.Y = _oldY; }
}
