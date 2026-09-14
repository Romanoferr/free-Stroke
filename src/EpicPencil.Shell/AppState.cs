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
    public event Action<TextObject>? TextAdded;
    public event Action<IReadOnlyList<TextObject>>? TextsRemoved;
    public event Action<TextObject>? TextMoved;
    public event Action<TextObject>? TextChanged;
    public event Action<RectangleObject>? RectangleAdded;
    public event Action<IReadOnlyList<RectangleObject>>? RectanglesRemoved;
    public event Action<RectangleObject>? RectangleMoved;
    public event Action<CircleObject>? CircleAdded;
    public event Action<IReadOnlyList<CircleObject>>? CirclesRemoved;
    public event Action<CircleObject>? CircleMoved;
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

    // Tamanho da fonte p/ novos textos (DIPs; o commit converte p/ px globais
    // na escala do monitor do clique). Só afeta criações futuras.
    public float ActiveFontSizeDip { get; private set; } = 20f;

    public void SetActiveFontSize(float sizeDip)
    {
        ActiveFontSizeDip = Math.Clamp(sizeDip, 8f, 128f);
        Log.Info($"fonte texto {ActiveFontSizeDip:F0}dip");
    }

    public int StrokeCount => _doc.Count;
    public int PointCount => _doc.TotalPoints();
    public IReadOnlyList<Stroke> Strokes => _doc.Strokes;
    public int ScreenCount => _doc.ScreenCount;
    public IReadOnlyList<ScreenObject> Screens => _doc.Screens;
    public int TextCount => _doc.TextCount;
    public IReadOnlyList<TextObject> Texts => _doc.Texts;
    public int RectangleCount => _doc.RectangleCount;
    public IReadOnlyList<RectangleObject> Rectangles => _doc.Rectangles;
    public int CircleCount => _doc.CircleCount;
    public IReadOnlyList<CircleObject> Circles => _doc.Circles;
    public int? SelectedScreenId => _selected is { Kind: CanvasObjectKind.Screen } s ? s.Id : null;
    // Seleção de OBJETO (texto/captura) × seleção de REGIÃO (marquee p/
    // captura): mutuamente exclusivas — nunca dois highlights ambíguos
    // (REQ7). Estado único _selected (REQ2); os getters antigos derivam
    // dele p/ compatibilidade (Toolbar, export, self-test).
    public int? SelectedTextId => _selected is { Kind: CanvasObjectKind.Text } t ? t.Id : null;
    // Seleção genérica (REQ1/REQ10): um único slot (Kind, Id). Ids são
    // únicos entre tipos (Document.NextId global), o Kind diz qual lista.
    public SelectedObject? SelectedObject => _selected;
    public bool HasSelection => _selected.HasValue;
    private SelectedObject? _selected;
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

    // Largura em PX GLOBAIS (a superfície converte seu DIP local na borda):
    // o documento inteiro — pontos, tolerâncias, bounds, hit-test — vive em px
    // da tela virtual, idêntico ao DIP em 100%.
    public Stroke? AddFreehand(List<Pt> rawPoints, float widthPx)
    {
        if (rawPoints.Count == 0) return null;
        float tol = StrokeSpec.CommitTolerance(widthPx);
        var simplified = Rdp.Simplify(rawPoints, tol);
        if (simplified.Count == 0) return null;
        var stroke = new Stroke(_doc.NextId(), ActiveTool, ActiveColor, widthPx,
            StrokeSpec.DefaultOpacity(ActiveTool), simplified);
        _undo.Execute(new AddStrokeCommand(stroke), _doc);
        Log.Info($"stroke id={stroke.Id} tool={stroke.Tool} pts={stroke.PointCount} " +
            $"cor=#{stroke.Color.R:X2}{stroke.Color.G:X2}{stroke.Color.B:X2} w={stroke.WidthPx:F1}px");
        StrokeAdded?.Invoke(stroke);
        StateChanged?.Invoke();
        return stroke;
    }

    // Formas saem sempre opacas: a polilinha assada da seta sobrepõe o fuste,
    // e sobreposição com alfa escureceria a região (mesmo bug do marker).
    public Stroke? AddShape(ToolKind tool, Pt a, Pt b, float widthPx)
    {
        if (tool != ToolKind.Line && tool != ToolKind.Arrow) return null;
        var points = tool == ToolKind.Line
            ? ShapeBuilder.BuildLine(a, b)
            : ShapeBuilder.BuildArrow(a, b, widthPx);
        var stroke = new Stroke(_doc.NextId(), tool, Presets[tool].Color, widthPx, 1f, points);
        _undo.Execute(new AddStrokeCommand(stroke), _doc);
        Log.Info($"shape id={stroke.Id} tool={tool} pts={stroke.PointCount}");
        StrokeAdded?.Invoke(stroke);
        StateChanged?.Invoke();
        return stroke;
    }

    public int EraseSegment(Pt a, Pt b)
    {
        var hit = HitTest.PickEraseSegment(_doc, a, b);
        var hitTexts = HitTest.PickEraseTexts(_doc.Texts, a, b);
        var hitRects = HitTest.PickEraseRectangles(_doc.Rectangles, a, b);
        var hitCircles = HitTest.PickEraseCircles(_doc.Circles, a, b);
        if (hit.Count == 0 && hitTexts.Count == 0 && hitRects.Count == 0 && hitCircles.Count == 0) return 0;
        // Um comando por gesto (Composite): um Ctrl+Z desfaz tudo de uma vez.
        var cmds = new List<IDocCommand>(4);
        if (hit.Count > 0) cmds.Add(new EraseStrokesCommand(hit, _doc));
        if (hitTexts.Count > 0) cmds.Add(new EraseTextsCommand(hitTexts, _doc));
        if (hitRects.Count > 0) cmds.Add(new EraseRectanglesCommand(hitRects, _doc));
        if (hitCircles.Count > 0) cmds.Add(new EraseCirclesCommand(hitCircles, _doc));
        IDocCommand cmd = cmds.Count == 1 ? cmds[0] : new CompositeCommand(cmds.ToArray());
        _undo.Execute(cmd, _doc);
        Log.Info($"borracha removeu {hit.Count} stroke(s), {hitTexts.Count} texto(s), " +
            $"{hitRects.Count} ret(s) e {hitCircles.Count} circ(s)");
        if (hit.Count > 0) StrokesRemoved?.Invoke(hit);
        if (hitTexts.Count > 0)
        {
            if (_selected is { Kind: CanvasObjectKind.Text } sel && hitTexts.Any(t => t.Id == sel.Id))
                _selected = null;
            TextsRemoved?.Invoke(hitTexts);
        }
        if (hitRects.Count > 0)
        {
            if (_selected is { Kind: CanvasObjectKind.Rectangle } selR && hitRects.Any(r => r.Id == selR.Id))
                _selected = null;
            RectanglesRemoved?.Invoke(hitRects);
        }
        if (hitCircles.Count > 0)
        {
            if (_selected is { Kind: CanvasObjectKind.Circle } selC && hitCircles.Any(c => c.Id == selC.Id))
                _selected = null;
            CirclesRemoved?.Invoke(hitCircles);
        }
        StateChanged?.Invoke();
        return hit.Count + hitTexts.Count + hitRects.Count + hitCircles.Count;
    }

    public void Clear()
    {
        if (_doc.Count == 0 && _doc.TextCount == 0 && _doc.RectangleCount == 0 && _doc.CircleCount == 0) return;
        var cmd = new ClearAllCommand(_doc);
        _undo.Execute(cmd, _doc);
        _selected = null; // objetos selecionados sumiram junto
        Log.Info($"clear removeu {cmd.Affected.Count} stroke(s), {cmd.AffectedTexts.Count} texto(s), " +
            $"{cmd.AffectedRectangles.Count} ret(s) e {cmd.AffectedCircles.Count} circ(s)");
        if (cmd.Affected.Count > 0) StrokesRemoved?.Invoke(cmd.Affected);
        if (cmd.AffectedTexts.Count > 0) TextsRemoved?.Invoke(cmd.AffectedTexts);
        if (cmd.AffectedRectangles.Count > 0) RectanglesRemoved?.Invoke(cmd.AffectedRectangles);
        if (cmd.AffectedCircles.Count > 0) CirclesRemoved?.Invoke(cmd.AffectedCircles);
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
            case AddTextCommand: // undo de AddText remove
                TextsRemoved?.Invoke(cmd.AffectedTexts);
                if (_selected is { Kind: CanvasObjectKind.Text } selT && cmd.AffectedTexts.Any(t => t.Id == selT.Id))
                    _selected = null;
                break;
            case MoveTextCommand: // undo do move volta à origem
                foreach (var t in cmd.AffectedTexts) TextMoved?.Invoke(t);
                break;
            case UpdateTextCommand: // undo da edição restaura anterior
                foreach (var t in cmd.AffectedTexts) TextChanged?.Invoke(t);
                break;
            case AddScreenCommand:
                ScreensRemoved?.Invoke(cmd.AffectedScreens);
                if (_selected is { Kind: CanvasObjectKind.Screen } selS && cmd.AffectedScreens.Any(s => s.Id == selS.Id))
                    _selected = null;
                break;
            case MoveScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenMoved?.Invoke(s);
                break;
            case AddRectangleCommand: // undo de Add remove
                RectanglesRemoved?.Invoke(cmd.AffectedRectangles);
                if (_selected is { Kind: CanvasObjectKind.Rectangle } selR
                    && cmd.AffectedRectangles.Any(r => r.Id == selR.Id))
                    _selected = null;
                break;
            case AddCircleCommand: // undo de Add remove
                CirclesRemoved?.Invoke(cmd.AffectedCircles);
                if (_selected is { Kind: CanvasObjectKind.Circle } selC
                    && cmd.AffectedCircles.Any(c => c.Id == selC.Id))
                    _selected = null;
                break;
            case MoveShapeCommand: // undo do move volta à origem
                foreach (var r in cmd.AffectedRectangles) RectangleMoved?.Invoke(r);
                foreach (var c in cmd.AffectedCircles) CircleMoved?.Invoke(c);
                break;
            default: // undo de Erase/Clear/RemoveScreen restaura
                foreach (var s in cmd.Affected) StrokeAdded?.Invoke(s);
                foreach (var s in cmd.AffectedScreens) ScreenAdded?.Invoke(s);
                foreach (var t in cmd.AffectedTexts) TextAdded?.Invoke(t);
                foreach (var r in cmd.AffectedRectangles) RectangleAdded?.Invoke(r);
                foreach (var c in cmd.AffectedCircles) CircleAdded?.Invoke(c);
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
            case AddTextCommand:
                foreach (var t in cmd.AffectedTexts) TextAdded?.Invoke(t);
                break;
            case MoveTextCommand:
                foreach (var t in cmd.AffectedTexts) TextMoved?.Invoke(t);
                break;
            case UpdateTextCommand:
                foreach (var t in cmd.AffectedTexts) TextChanged?.Invoke(t);
                break;
            case AddScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenAdded?.Invoke(s);
                break;
            case MoveScreenCommand:
                foreach (var s in cmd.AffectedScreens) ScreenMoved?.Invoke(s);
                break;
            case AddRectangleCommand:
                foreach (var r in cmd.AffectedRectangles) RectangleAdded?.Invoke(r);
                break;
            case AddCircleCommand:
                foreach (var c in cmd.AffectedCircles) CircleAdded?.Invoke(c);
                break;
            case MoveShapeCommand:
                foreach (var r in cmd.AffectedRectangles) RectangleMoved?.Invoke(r);
                foreach (var c in cmd.AffectedCircles) CircleMoved?.Invoke(c);
                break;
            default: // redo repete o Do: Erase/Clear/RemoveScreen remove
                if (cmd.Affected.Count > 0) StrokesRemoved?.Invoke(cmd.Affected);
                if (cmd.AffectedScreens.Count > 0) ScreensRemoved?.Invoke(cmd.AffectedScreens);
                if (cmd.AffectedTexts.Count > 0) TextsRemoved?.Invoke(cmd.AffectedTexts);
                if (cmd.AffectedRectangles.Count > 0) RectanglesRemoved?.Invoke(cmd.AffectedRectangles);
                if (cmd.AffectedCircles.Count > 0) CirclesRemoved?.Invoke(cmd.AffectedCircles);
                break;
        }
        StateChanged?.Invoke();
    }

    // Object Selection (objeto existente: texto/captura) × Region Selection
    // (marquee → captura de pixels): o marquee nunca seleciona objeto, só
    // cria região; o clique num objeto nunca inicia região (REQ7/REQ8).
    public void SelectObject(SelectedObject? sel)
    {
        _selected = sel;
        if (sel.HasValue)
            Log.Info($"objeto selecionado {sel.Value.Kind} id={sel.Value.Id}");
        StateChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (!_selected.HasValue) return;
        _selected = null;
        Log.Info("seleção limpa");
        StateChanged?.Invoke();
    }

    public void SelectScreen(int? id)
    {
        _selected = id.HasValue ? new SelectedObject(CanvasObjectKind.Screen, id.Value) : null;
        if (id.HasValue)
        {
            Log.Info($"captura selecionada id={id}");
        }
        // null = marquee novo abandona seleção de objeto (REQ7)
        StateChanged?.Invoke();
    }

    public void SelectText(int? id)
    {
        // null = desseleciona (Escape/área vazia); valor = objeto (REQ2)
        _selected = id.HasValue ? new SelectedObject(CanvasObjectKind.Text, id.Value) : null;
        if (id.HasValue)
        {
            Log.Info($"texto selecionado id={id}");
        }
        StateChanged?.Invoke();
    }

    public ScreenObject? AddScreen(byte[] bgra, int pixelWidth, int pixelHeight, RectD globalPxRect)
    {
        if (bgra.Length != 4 * pixelWidth * pixelHeight)
        {
            Log.Warn("captura descartada: bytes incompatíveis");
            return null;
        }
        var screen = new ScreenObject(_doc.NextId(), globalPxRect.X, globalPxRect.Y,
            globalPxRect.Width, globalPxRect.Height, pixelWidth, pixelHeight, bgra);
        _undo.Execute(new AddScreenCommand(screen), _doc);
        Log.Info($"captura id={screen.Id} {pixelWidth}x{pixelHeight}px em ({globalPxRect.X:F0},{globalPxRect.Y:F0})");
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

    // Delete genérico (REQ5): remove o objeto selecionado via o comando
    // do próprio tipo (mesmo UndoStack, sem segundo sistema — REQ6).
    public bool DeleteSelectedObject() => _selected switch
    {
        { Kind: CanvasObjectKind.Text } => DeleteSelectedText(),
        { Kind: CanvasObjectKind.Screen } => DeleteSelectedScreen(),
        { Kind: CanvasObjectKind.Rectangle } => DeleteSelectedRectangle(),
        { Kind: CanvasObjectKind.Circle } => DeleteSelectedCircle(),
        _ => false,
    };

    public bool DeleteSelectedText()
    {
        if (_selected is not { Kind: CanvasObjectKind.Text } sel) return false;
        var t = _doc.FindText(sel.Id);
        if (t is null) { _selected = null; return false; }
        _undo.Execute(new EraseTextsCommand(new[] { t }, _doc), _doc);
        Log.Info($"texto id={t.Id} removido (Delete)");
        TextsRemoved?.Invoke(new[] { t });
        _selected = null;
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSelectedScreen()
    {
        if (_selected is not { Kind: CanvasObjectKind.Screen } sel) return false;
        var s = _doc.FindScreen(sel.Id);
        if (s is null) { _selected = null; return false; }
        _undo.Execute(new RemoveScreenCommand(new[] { s }, _doc), _doc);
        Log.Info($"captura id={s.Id} removida");
        ScreensRemoved?.Invoke(new[] { s });
        _selected = null;
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSelectedRectangle()
    {
        if (_selected is not { Kind: CanvasObjectKind.Rectangle } sel) return false;
        var r = _doc.FindRectangle(sel.Id);
        if (r is null) { _selected = null; return false; }
        _undo.Execute(new EraseRectanglesCommand(new[] { r }, _doc), _doc);
        Log.Info($"retângulo id={r.Id} removido (Delete)");
        RectanglesRemoved?.Invoke(new[] { r });
        _selected = null;
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSelectedCircle()
    {
        if (_selected is not { Kind: CanvasObjectKind.Circle } sel) return false;
        var c = _doc.FindCircle(sel.Id);
        if (c is null) { _selected = null; return false; }
        _undo.Execute(new EraseCirclesCommand(new[] { c }, _doc), _doc);
        Log.Info($"círculo id={c.Id} removido (Delete)");
        CirclesRemoved?.Invoke(new[] { c });
        _selected = null;
        StateChanged?.Invoke();
        return true;
    }

    // Commit de forma (bounds em px globais; abaixo do mínimo = cancelado,
    // sem objeto e sem undo — REQ4). Cor/espessura congeladas do preset da
    // ferramenta: objetos existentes nunca mudam (REQ11).
    public RectangleObject? AddRectangle(RectD globalPxBounds, Rgba color, float strokeWidthPx)
    {
        if (!ShapeGeometry.MeetsMinSize(globalPxBounds))
        {
            Log.Info("retângulo mínimo descartado (clique sem arrasto, sem objeto, sem undo)");
            return null;
        }
        var rect = new RectangleObject(_doc.NextId(), globalPxBounds.X, globalPxBounds.Y,
            globalPxBounds.Width, globalPxBounds.Height, color, strokeWidthPx);
        _undo.Execute(new AddRectangleCommand(rect), _doc);
        Log.Info($"retângulo id={rect.Id} {globalPxBounds.Width:F0}x{globalPxBounds.Height:F0}px " +
            $"em ({globalPxBounds.X:F0},{globalPxBounds.Y:F0})");
        RectangleAdded?.Invoke(rect);
        StateChanged?.Invoke();
        return rect;
    }

    public CircleObject? AddCircle(RectD globalPxBounds, Rgba color, float strokeWidthPx)
    {
        if (!ShapeGeometry.MeetsMinSize(globalPxBounds))
        {
            Log.Info("círculo mínimo descartado (clique sem arrasto, sem objeto, sem undo)");
            return null;
        }
        var circle = new CircleObject(_doc.NextId(), globalPxBounds.X, globalPxBounds.Y,
            globalPxBounds.Width, globalPxBounds.Height, color, strokeWidthPx);
        _undo.Execute(new AddCircleCommand(circle), _doc);
        Log.Info($"círculo id={circle.Id} {globalPxBounds.Width:F0}x{globalPxBounds.Height:F0}px " +
            $"em ({globalPxBounds.X:F0},{globalPxBounds.Y:F0})");
        CircleAdded?.Invoke(circle);
        StateChanged?.Invoke();
        return circle;
    }

    // Move genérico das formas (REQ8): modelo só commita no drop (o arrasto
    // ao vivo move só o visual). Sem mudança = sem comando.
    public bool MoveShape(CanvasObjectKind kind, int id, float x, float y)
    {
        IMovableShape? target = kind switch
        {
            CanvasObjectKind.Rectangle => _doc.FindRectangle(id),
            CanvasObjectKind.Circle => _doc.FindCircle(id),
            _ => null,
        };
        if (target is null || (target.X == x && target.Y == y)) return false;
        _undo.Execute(new MoveShapeCommand(target, target.X, target.Y, x, y), _doc);
        Log.Info($"forma {kind} id={id} movida para ({x:F0},{y:F0})");
        if (target is RectangleObject r) RectangleMoved?.Invoke(r);
        else if (target is CircleObject c) CircleMoved?.Invoke(c);
        StateChanged?.Invoke();
        return true;
    }

    // Commit de texto (posição/tamanho/métricas em px globais; métricas medidas
    // pela Shell na fonte real). Vazio nunca vira objeto: null sem tocar no undo.
    public TextObject? AddText(string content, float x, float y,
        float fontSizePx, string fontFamily, Rgba color, float widthPx, float heightPx)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            Log.Info("texto vazio descartado (sem objeto, sem undo)");
            return null;
        }
        var text = new TextObject(_doc.NextId(), x, y, content.TrimEnd(),
            fontSizePx, fontFamily, color, widthPx, heightPx);
        _undo.Execute(new AddTextCommand(text), _doc);
        Log.Info($"texto id={text.Id} \"{text.Content}\" {fontSizePx:F0}px em ({x:F0},{y:F0})");
        TextAdded?.Invoke(text);
        StateChanged?.Invoke();
        return text;
    }

    // Arrastar do texto selecionado: modelo só commita no drop (o arrasto ao
    // vivo move só o visual). Sem mudança = sem comando.
    public bool MoveText(int id, float x, float y)
    {
        var t = _doc.Texts.FirstOrDefault(t => t.Id == id);
        if (t is null || (t.X == x && t.Y == y)) return false;
        _undo.Execute(new MoveTextCommand(t, t.X, t.Y, x, y), _doc);
        Log.Info($"texto id={id} movido para ({x:F0},{y:F0})");
        TextMoved?.Invoke(t);
        StateChanged?.Invoke();
        return true;
    }

    // Reedição ou troca de tamanho: um comando por commit (um Ctrl+Z desfaz).
    // Sem mudança = false, sem comando. Métricas medidas pela Shell.
    public bool UpdateText(int id, string content, float fontSizePx, float widthPx, float heightPx)
    {
        var t = _doc.Texts.FirstOrDefault(t => t.Id == id);
        if (t is null || string.IsNullOrWhiteSpace(content)) return false;
        content = content.TrimEnd();
        if (t.Content == content && t.FontSizePx == fontSizePx) return false;
        _undo.Execute(new UpdateTextCommand(t, content, fontSizePx, widthPx, heightPx), _doc);
        Log.Info($"texto id={id} atualizado \"{content}\" {fontSizePx:F0}px");
        TextChanged?.Invoke(t);
        StateChanged?.Invoke();
        return true;
    }
}
