// Comandos de captura: mesmo padrão dos strokes (nunca snapshot de tela).
// Move guarda old/new e aplica via Do/Undo — o arrasto ao vivo move só o
// VISUAL (Offset), o modelo só muda no commit (sem spam de comandos).

using EpicPencil.Core;

namespace EpicPencil.Core;

public sealed class AddScreenCommand : IDocCommand
{
    private readonly ScreenObject _screen;
    public AddScreenCommand(ScreenObject screen) => _screen = screen;
    public int RetainedPoints => (int)(_screen.ByteSize / 1024); // teto do undo em escala aprox.
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<ScreenObject> AffectedScreens => [_screen];
    public void Do(Document doc) => doc.AddScreen(_screen);
    public void Undo(Document doc) => doc.RemoveScreen(_screen);
}

public sealed class RemoveScreenCommand : IDocCommand
{
    private readonly List<(ScreenObject Screen, int Index)> _removed = new();
    public RemoveScreenCommand(IReadOnlyList<ScreenObject> removed, Document doc)
    {
        var all = doc.Screens;
        foreach (var s in removed)
            _removed.Add((s, IndexOf(all, s)));
    }
    public int RetainedPoints
    {
        get
        {
            long n = 0;
            foreach (var r in _removed) n += r.Screen.ByteSize;
            return (int)(n / 1024);
        }
    }
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<ScreenObject> AffectedScreens => _removed.Select(r => r.Screen).ToList();
    public void Do(Document doc)
    {
        foreach (var r in _removed) doc.RemoveScreen(r.Screen);
    }
    public void Undo(Document doc)
    {
        var ordered = new List<(ScreenObject Screen, int Index)>(_removed);
        ordered.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var r in ordered)
            doc.InsertScreen(r.Screen, Math.Min(r.Index, doc.ScreenCount));
    }
    private static int IndexOf(IReadOnlyList<ScreenObject> all, ScreenObject s)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], s)) return i;
        return all.Count;
    }
}

public sealed class MoveScreenCommand : IDocCommand
{
    private readonly ScreenObject _screen;
    private readonly float _oldX, _oldY, _newX, _newY;
    public MoveScreenCommand(ScreenObject screen, float oldX, float oldY, float newX, float newY)
    {
        _screen = screen;
        _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }
    public int RetainedPoints => 0; // bitmap continua no Document; nada retido a mais
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<ScreenObject> AffectedScreens => [_screen];
    public void Do(Document doc) { _screen.X = _newX; _screen.Y = _newY; }
    public void Undo(Document doc) { _screen.X = _oldX; _screen.Y = _oldY; }
}
