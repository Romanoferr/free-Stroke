// Single source of truth da Shell (UI-free: sem using System.Windows).
// A OverlayWindow e a Toolbar apenas observam e chamam métodos — o estado
// nunca é duplicado nas janelas.

using EpicPencil.Core;

namespace EpicPencil.Shell;

public sealed class AppState
{
    private readonly Document _doc = new();
    private readonly UndoStack _undo = new();

    public event Action<Stroke>? StrokeAdded;
    public event Action<IReadOnlyList<Stroke>>? StrokesRemoved;
    public event Action? StateChanged;

    public ToolKind ActiveTool { get; private set; } = ToolKind.Pen;
    public bool IsDrawMode { get; private set; } = true;
    public bool InkVisible { get; private set; } = true;

    public Rgba PenColor { get; set; } = new(255, 0, 0);
    public float PenWidth { get; set; } = 4f;

    public int StrokeCount => _doc.Count;
    public int PointCount => _doc.TotalPoints();
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public void SetTool(ToolKind tool)
    {
        ActiveTool = tool;
        StateChanged?.Invoke();
    }

    public void SetDrawMode(bool draw)
    {
        IsDrawMode = draw;
        StateChanged?.Invoke();
    }

    public void SetInkVisible(bool visible)
    {
        InkVisible = visible;
        StateChanged?.Invoke();
    }

    public Stroke? AddFreehand(List<Pt> rawPoints)
    {
        if (rawPoints.Count == 0) return null;
        float tol = StrokeSpec.CommitTolerance(PenWidth);
        var simplified = Rdp.Simplify(rawPoints, tol);
        if (simplified.Count == 0) return null;
        var stroke = new Stroke(_doc.NextId(), ActiveTool, PenColor, PenWidth,
            StrokeSpec.DefaultOpacity(ActiveTool), simplified);
        _undo.Execute(new AddStrokeCommand(stroke), _doc);
        StrokeAdded?.Invoke(stroke);
        StateChanged?.Invoke();
        return stroke;
    }

    public int EraseSegment(Pt a, Pt b)
    {
        var hit = HitTest.PickEraseSegment(_doc, a, b);
        if (hit.Count == 0) return 0;
        _undo.Execute(new EraseStrokesCommand(hit, _doc), _doc);
        StrokesRemoved?.Invoke(hit);
        StateChanged?.Invoke();
        return hit.Count;
    }

    public void Clear()
    {
        if (_doc.Count == 0) return;
        var cmd = new ClearAllCommand(_doc);
        _undo.Execute(cmd, _doc);
        StrokesRemoved?.Invoke(cmd.Affected);
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!_undo.TryUndo(_doc, out var cmd) || cmd is null) return;
        // Undo de Add remove; undo de Erase/Clear restaura.
        if (cmd is AddStrokeCommand) StrokesRemoved?.Invoke(cmd.Affected);
        else foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!_undo.TryRedo(_doc, out var cmd) || cmd is null) return;
        // Redo repete o Do original: Add adiciona, Erase/Clear remove.
        if (cmd is AddStrokeCommand) foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
        else StrokesRemoved?.Invoke(cmd.Affected);
        StateChanged?.Invoke();
    }
}
