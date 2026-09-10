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
        // Undo de Add remove; undo de Erase/Clear restaura.
        if (cmd is AddStrokeCommand) StrokesRemoved?.Invoke(cmd.Affected);
        else foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!_undo.TryRedo(_doc, out var cmd) || cmd is null) { Log.Info("redo vazio"); return; }
        Log.Info($"redo {cmd.GetType().Name}");
        // Redo repete o Do original: Add adiciona, Erase/Clear remove.
        if (cmd is AddStrokeCommand) foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
        else StrokesRemoved?.Invoke(cmd.Affected);
        StateChanged?.Invoke();
    }
}
