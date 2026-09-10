// Single source of truth da Shell (UI-free: sem using System.Windows).
// A OverlayWindow e a Toolbar apenas observam e chamam métodos — o estado
// nunca é duplicado nas janelas.

using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public sealed class AppState
{
    private readonly Document _doc = new();
    private readonly UndoStack _undo = new();

    public event Action<Stroke>? StrokeAdded;
    public event Action<IReadOnlyList<Stroke>>? StrokesRemoved;
    public event Action<ScreenObject>? ScreenAdded;
    public event Action<IReadOnlyList<ScreenObject>>? ScreensRemoved;
    public event Action<ScreenObject>? ScreenMoved;
    public event Action? StateChanged;

    public ToolKind ActiveTool { get; private set; } = ToolKind.Pen;
    public bool IsDrawMode { get; private set; } = true;
    public bool InkVisible { get; private set; } = true;

    // Presets vivos por ferramenta (memoriza última cor/espessura de cada uma).
    // Valores iniciais vêm do schema de settings; persistência em arquivo entra depois.
    public Dictionary<ToolKind, ToolPreset> Presets { get; } =
        SettingsV1.WithDefaults().Presets;

    public Rgba ActiveColor
    {
        get => Presets[ActiveTool].Color;
        set
        {
            Presets[ActiveTool] = new ToolPreset { Color = value, WidthDip = ActiveWidth };
            Log.Info($"cor tool={ActiveTool} #{value.R:X2}{value.G:X2}{value.B:X2}");
        }
    }

    public float ActiveWidth
    {
        get => Presets[ActiveTool].WidthDip;
        set => Presets[ActiveTool] = new ToolPreset
        {
            Color = ActiveColor,
            WidthDip = Math.Clamp(value, 1f, 64f)
        };
    }

    public void SetActiveWidthPreset(int size)
    {
        ActiveWidth = StrokeSpec.WidthPreset(ActiveTool, size);
        Log.Info($"espessura tool={ActiveTool} w={ActiveWidth:F1}");
        StateChanged?.Invoke();
    }

    public int StrokeCount => _doc.Count;
    public int PointCount => _doc.TotalPoints();
    public int ScreenCount => _doc.ScreenCount;
    public IReadOnlyList<ScreenObject> Screens => _doc.Screens;
    public int? SelectedScreenId { get; private set; }
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public void SetTool(ToolKind tool)
    {
        if (ActiveTool == tool) return;
        ActiveTool = tool;
        Log.Info($"tool={tool}");
        StateChanged?.Invoke();
    }

    public void SetDrawMode(bool draw)
    {
        if (IsDrawMode == draw) return;
        IsDrawMode = draw;
        Log.Info(draw ? "modo=desenho" : "modo=interagir (click-through)");
        StateChanged?.Invoke();
    }

    public void SetInkVisible(bool visible)
    {
        if (InkVisible == visible) return;
        InkVisible = visible;
        Log.Info(visible ? "tinta visível" : "tinta escondida");
        StateChanged?.Invoke();
    }

    public Stroke? AddFreehand(List<Pt> rawPoints)
    {
        if (rawPoints.Count == 0) return null;
        float tol = StrokeSpec.CommitTolerance(ActiveWidth);
        var simplified = Rdp.Simplify(rawPoints, tol);
        if (simplified.Count == 0) return null;
        var stroke = new Stroke(_doc.NextId(), ActiveTool, ActiveColor, ActiveWidth,
            StrokeSpec.DefaultOpacity(ActiveTool), simplified);
        _undo.Execute(new AddStrokeCommand(stroke), _doc);
        Log.Info($"stroke id={stroke.Id} tool={stroke.Tool} pts={stroke.PointCount} " +
            $"cor=#{stroke.Color.R:X2}{stroke.Color.G:X2}{stroke.Color.B:X2} w={stroke.WidthDip:F1}");
        StrokeAdded?.Invoke(stroke);
        StateChanged?.Invoke();
        return stroke;
    }

    // Formas saem sempre opacas: a polilinha assada da seta sobrepõe o fuste,
    // e sobreposição com alfa escureceria a região (mesmo bug do marker).
    public Stroke? AddShape(ToolKind tool, Pt a, Pt b)
    {
        if (tool != ToolKind.Line && tool != ToolKind.Arrow) return null;
        float width = Presets[tool].WidthDip;
        var points = tool == ToolKind.Line
            ? ShapeBuilder.BuildLine(a, b)
            : ShapeBuilder.BuildArrow(a, b, width);
        var stroke = new Stroke(_doc.NextId(), tool, Presets[tool].Color, width, 1f, points);
        _undo.Execute(new AddStrokeCommand(stroke), _doc);
        Log.Info($"shape id={stroke.Id} tool={tool} pts={stroke.PointCount}");
        StrokeAdded?.Invoke(stroke);
        StateChanged?.Invoke();
        return stroke;
    }

    public int EraseSegment(Pt a, Pt b)
    {
        var hit = HitTest.PickEraseSegment(_doc, a, b);
        if (hit.Count == 0) return 0;
        _undo.Execute(new EraseStrokesCommand(hit, _doc), _doc);
        Log.Info($"borracha removeu {hit.Count} stroke(s)");
        StrokesRemoved?.Invoke(hit);
        StateChanged?.Invoke();
        return hit.Count;
    }

    public void Clear()
    {
        if (_doc.Count == 0) return;
        var cmd = new ClearAllCommand(_doc);
        _undo.Execute(cmd, _doc);
        Log.Info($"clear removeu {cmd.Affected.Count} stroke(s)");
        StrokesRemoved?.Invoke(cmd.Affected);
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!_undo.TryUndo(_doc, out var cmd) || cmd is null) { Log.Info("undo vazio"); return; }
        Log.Info($"undo {cmd.GetType().Name}");
        switch (cmd)
        {
            case AddStrokeCommand: // undo de Add remove
                StrokesRemoved?.Invoke(cmd.Affected);
                break;
            case AddScreenCommand:
                ScreensRemoved?.Invoke(cmd.AffectedScreens);
                if (SelectedScreenId.HasValue && cmd.AffectedScreens.Any(s => s.Id == SelectedScreenId))
                    SelectedScreenId = null;
                break;
            case MoveScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenMoved?.Invoke(s);
                break;
            default: // undo de Erase/Clear/RemoveScreen restaura
                foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
                foreach (var s in cmd.AffectedScreens) ScreenAdded?.Invoke(s);
                break;
        }
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!_undo.TryRedo(_doc, out var cmd) || cmd is null) { Log.Info("redo vazio"); return; }
        Log.Info($"redo {cmd.GetType().Name}");
        switch (cmd)
        {
            case AddStrokeCommand:
                foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
                break;
            case AddScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenAdded?.Invoke(s);
                break;
            case MoveScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenMoved?.Invoke(s);
                break;
            default: // redo repete o Do: Erase/Clear/RemoveScreen remove
                if (cmd.Affected.Count > 0) StrokesRemoved?.Invoke(cmd.Affected);
                if (cmd.AffectedScreens.Count > 0) ScreensRemoved?.Invoke(cmd.AffectedScreens);
                break;
        }
        StateChanged?.Invoke();
    }

    public void SelectScreen(int? id)
    {
        SelectedScreenId = id;
        if (id.HasValue) Log.Info($"captura selecionada id={id}");
        StateChanged?.Invoke();
    }

    public ScreenObject? AddScreen(byte[] bgra, int pixelWidth, int pixelHeight, RectD dipRect)
    {
        if (bgra.Length != 4 * pixelWidth * pixelHeight)
        {
            Log.Warn("captura descartada: bytes incompatíveis");
            return null;
        }
        var screen = new ScreenObject(_doc.NextId(), dipRect.X, dipRect.Y,
            dipRect.Width, dipRect.Height, pixelWidth, pixelHeight, bgra);
        _undo.Execute(new AddScreenCommand(screen), _doc);
        Log.Info($"captura id={screen.Id} {pixelWidth}x{pixelHeight}px em ({dipRect.X:F0},{dipRect.Y:F0})");
        ScreenAdded?.Invoke(screen);
        SelectScreen(screen.Id);
        return screen;
    }

    public bool MoveScreen(int id, float x, float y)
    {
        var s = _doc.FindScreen(id);
        if (s is null || (s.X == x && s.Y == y)) return false;
        _undo.Execute(new MoveScreenCommand(s, s.X, s.Y, x, y), _doc);
        Log.Info($"captura id={id} movida para ({x:F0},{y:F0})");
        ScreenMoved?.Invoke(s);
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSelectedScreen()
    {
        if (!SelectedScreenId.HasValue) return false;
        var s = _doc.FindScreen(SelectedScreenId.Value);
        if (s is null) { SelectedScreenId = null; return false; }
        _undo.Execute(new RemoveScreenCommand(new[] { s }, _doc), _doc);
        Log.Info($"captura id={s.Id} removida");
        ScreensRemoved?.Invoke(new[] { s });
        SelectedScreenId = null;
        StateChanged?.Invoke();
        return true;
    }
}
