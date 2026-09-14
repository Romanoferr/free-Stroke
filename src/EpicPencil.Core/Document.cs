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
    private readonly List<ScreenObject> _screens = new();
    private readonly List<TextObject> _texts = new();
    private readonly List<RectangleObject> _rectangles = new();
    private readonly List<CircleObject> _circles = new();
    private int _nextId = 1;

    public IReadOnlyList<Stroke> Strokes => _strokes;
    public IReadOnlyList<ScreenObject> Screens => _screens;
    public IReadOnlyList<TextObject> Texts => _texts;
    public IReadOnlyList<RectangleObject> Rectangles => _rectangles;
    public IReadOnlyList<CircleObject> Circles => _circles;
    public Camera Camera { get; private set; } = Camera.Identity;
    public int Count => _strokes.Count;
    public int ScreenCount => _screens.Count;
    public int TextCount => _texts.Count;
    public int RectangleCount => _rectangles.Count;
    public int CircleCount => _circles.Count;

    public int NextId() => _nextId++;

    internal void Insert(Stroke stroke, int index) => _strokes.Insert(index, stroke);
    internal void Add(Stroke stroke) => _strokes.Add(stroke);
    internal bool Remove(Stroke stroke) => _strokes.Remove(stroke);
    internal void Clear() => _strokes.Clear();

    internal void AddScreen(ScreenObject screen) => _screens.Add(screen);
    internal void InsertScreen(ScreenObject screen, int index) => _screens.Insert(index, screen);
    internal bool RemoveScreen(ScreenObject screen) => _screens.Remove(screen);

    internal void AddText(TextObject text) => _texts.Add(text);
    internal void InsertText(TextObject text, int index) => _texts.Insert(index, text);
    internal bool RemoveText(TextObject text) => _texts.Remove(text);

    internal void AddRectangle(RectangleObject rect) => _rectangles.Add(rect);
    internal void InsertRectangle(RectangleObject rect, int index) => _rectangles.Insert(index, rect);
    internal bool RemoveRectangle(RectangleObject rect) => _rectangles.Remove(rect);

    internal void AddCircle(CircleObject circle) => _circles.Add(circle);
    internal void InsertCircle(CircleObject circle, int index) => _circles.Insert(index, circle);
    internal bool RemoveCircle(CircleObject circle) => _circles.Remove(circle);

    public ScreenObject? FindScreen(int id)
    {
        foreach (var s in _screens)
            if (s.Id == id) return s;
        return null;
    }

    public TextObject? FindText(int id)
    {
        foreach (var t in _texts)
            if (t.Id == id) return t;
        return null;
    }

    public RectangleObject? FindRectangle(int id)
    {
        foreach (var r in _rectangles)
            if (r.Id == id) return r;
        return null;
    }

    public CircleObject? FindCircle(int id)
    {
        foreach (var c in _circles)
            if (c.Id == id) return c;
        return null;
    }

    public int TotalPoints()
    {
        int n = 0;
        foreach (var s in _strokes) n += s.PointCount;
        return n;
    }
}
