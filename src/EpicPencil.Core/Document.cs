// Documento = lista ordenada (z-order) + Camera travada em identidade no MVP.
// A struct Camera permanece para não quebrar serialização futura, mas Pan/Zoom
// fora de (0,0,1) são rejeitados até o pós-MVP (decisão R3 do Review).

namespace EpicPencil.Core;

public readonly record struct Camera(float PanX, float PanY, float Zoom)
{
    public static readonly Camera Identity = new(0, 0, 1);
    public bool IsIdentity => PanX == 0 && PanY == 0 && Zoom == 1;
}

public sealed class Document
{
    private readonly List<Stroke> _strokes = new();
    private int _nextId = 1;

    public IReadOnlyList<Stroke> Strokes => _strokes;
    public Camera Camera { get; private set; } = Camera.Identity;
    public int Count => _strokes.Count;

    public int NextId() => _nextId++;

    internal void Insert(Stroke stroke, int index) => _strokes.Insert(index, stroke);
    internal void Add(Stroke stroke) => _strokes.Add(stroke);
    internal bool Remove(Stroke stroke) => _strokes.Remove(stroke);
    internal void Clear() => _strokes.Clear();

    public int TotalPoints()
    {
        int n = 0;
        foreach (var s in _strokes) n += s.PointCount;
        return n;
    }
}
