// Undo por COMANDOS, nunca snapshots de bitmap (ADR-06).
// Teto duplo: nº de comandos + nº de pontos retidos. Sem teto de pontos,
// 50 strokes gigantes estouram a RAM — o limite por contagem sozinho é falho.

namespace EpicPencil.Core;

public interface IDocCommand
{
    void Do(Document doc);
    void Undo(Document doc);
    int RetainedPoints { get; }
    // Strokes tocados pelo comando — a Shell sincroniza os visuals sem rebuild.
    IReadOnlyList<Stroke> Affected { get; }
    // Capturas tocadas. Default vazio: comandos de stroke não precisam mudar.
    IReadOnlyList<ScreenObject> AffectedScreens => Array.Empty<ScreenObject>();
}

public sealed class AddStrokeCommand : IDocCommand
{
    private readonly Stroke _stroke;
    public AddStrokeCommand(Stroke stroke) => _stroke = stroke;
    public int RetainedPoints => _stroke.PointCount;
    public IReadOnlyList<Stroke> Affected => [_stroke];
    public void Do(Document doc) => doc.Add(_stroke);
    public void Undo(Document doc) => doc.Remove(_stroke);
}

public sealed class EraseStrokesCommand : IDocCommand
{
    private readonly List<(Stroke Stroke, int Index)> _removed = new();
    public EraseStrokesCommand(IReadOnlyList<Stroke> removed, Document doc)
    {
        var all = doc.Strokes;
        foreach (var s in removed)
            _removed.Add((s, IndexOf(all, s)));
    }
    public int RetainedPoints
    {
        get
        {
            int n = 0;
            foreach (var r in _removed) n += r.Stroke.PointCount;
            return n;
        }
    }
    public IReadOnlyList<Stroke> Affected => _removed.Select(r => r.Stroke).ToList();
    public void Do(Document doc)
    {
        foreach (var r in _removed) doc.Remove(r.Stroke);
    }
    public void Undo(Document doc)
    {
        // Reinsere em ordem crescente de índice para restaurar z-order exato.
        var ordered = new List<(Stroke Stroke, int Index)>(_removed);
        ordered.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var r in ordered)
            doc.Insert(r.Stroke, Math.Min(r.Index, doc.Count));
    }
    private static int IndexOf(IReadOnlyList<Stroke> all, Stroke s)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], s)) return i;
        return all.Count;
    }
}

public sealed class ClearAllCommand : IDocCommand
{
    private readonly List<Stroke> _snapshot;
    public ClearAllCommand(Document doc) => _snapshot = new List<Stroke>(doc.Strokes);
    public int RetainedPoints
    {
        get
        {
            int n = 0;
            foreach (var s in _snapshot) n += s.PointCount;
            return n;
        }
    }
    public IReadOnlyList<Stroke> Affected => _snapshot;
    public void Do(Document doc) => doc.Clear();
    public void Undo(Document doc)
    {
        foreach (var s in _snapshot) doc.Add(s);
    }
}

public sealed class UndoStack
{
    public const int MaxCommands = 50;
    public const int MaxRetainedPoints = 250_000;

    private readonly List<IDocCommand> _undo = new();
    private readonly Stack<IDocCommand> _redo = new();
    private int _retainedPoints;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IDocCommand cmd, Document doc)
    {
        cmd.Do(doc);
        _undo.Add(cmd);
        _redo.Clear();
        _retainedPoints += cmd.RetainedPoints;
        EnforceCaps();
    }

    public bool TryUndo(Document doc) => TryUndo(doc, out _);

    public bool TryUndo(Document doc, out IDocCommand? undone)
    {
        if (_undo.Count == 0) { undone = null; return false; }
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo(doc);
        _retainedPoints -= cmd.RetainedPoints;
        _redo.Push(cmd);
        undone = cmd;
        return true;
    }

    public bool TryRedo(Document doc) => TryRedo(doc, out _);

    public bool TryRedo(Document doc, out IDocCommand? redone)
    {
        if (_redo.Count == 0) { redone = null; return false; }
        var cmd = _redo.Pop();
        cmd.Do(doc);
        _undo.Add(cmd);
        _retainedPoints += cmd.RetainedPoints;
        EnforceCaps();
        redone = cmd;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _retainedPoints = 0;
    }

    private void EnforceCaps()
    {
        while ((_undo.Count > MaxCommands || _retainedPoints > MaxRetainedPoints) && _undo.Count > 0)
        {
            _retainedPoints -= _undo[0].RetainedPoints;
            _undo.RemoveAt(0);
        }
        if (_retainedPoints < 0) _retainedPoints = 0;
    }
}
