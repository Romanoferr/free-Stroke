// Superfície de tinta: hospeda os visuals + captura stylus/mouse.
// Decisões não óbvias:
//  - Funil único BeginAt/MoveAt/EndAt para Stylus* e Mouse*: o WPF dispara os
//    DOIS para o mesmo clique físico (promoção síncrona). Dedup via InputDedup
//    (mouse ignorado se stylus < 80 ms) — sem isso, ambientes sem promoção não
//    desenham e ambientes com promoção desenham dobrado.
//  - Preview reconstrói a geometria inteira do stroke ativo por move: O(n) por
//    move é aceitável porque o InputFilter mantém n em centenas (medido no p50).
//  - HitTestCore sempre positivo NÃO basta sozinho: no path real (WM_NCHITTEST)
//    o WPF faz bounds pre-check (união dos filhos) antes de chamá-lo — canvas
//    vazio tem bounds vazios e vira HTTRANSPARENT mesmo sem WS_EX_TRANSPARENT
//    (bug real: cliques atravessavam com overlay topo/visível). Por isso existe
//    _hit: retângulo Transparent full-bleed (invisível, mas atingível) que dá
//    bounds de hit-test. Brush Transparent (≠ null) participa do hit-test.

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public sealed class InkSurface : FrameworkElement
{
    private readonly DrawingVisual _hit = new();
    private readonly ContainerVisual _screens = new(); // capturas (ABAIXO da tinta: anota-se sobre a cópia)
    private readonly ContainerVisual _finalized = new();
    private readonly DrawingVisual _active = new();
    private readonly DrawingVisual _selection = new();
    private readonly DrawingVisual _cursorRing = new(); // F-14: ferramenta+espessura+cor no ponteiro
    private readonly Dictionary<int, DrawingVisual> _screenVisuals = new();
    private readonly Dictionary<int, ImageSource> _screenImages = new();
    private readonly Dictionary<int, DrawingVisual> _map = new();
    private readonly Dictionary<int, DrawingVisual> _textVisuals = new();
    private readonly Dictionary<int, DrawingVisual> _rectVisuals = new();
    private readonly Dictionary<int, DrawingVisual> _circleVisuals = new();
    private readonly double[] _samples = new double[128];
    private int _sampleCount;

    private AppState? _state;
    private List<Pt> _raw = new();
    private bool _drawing;
    private bool _erasing;
    private bool _shaping;
    private Pt _anchor;
    private Pt _lastErase;
    private double _lastStylusMs = double.NaN;
    private DateTime _lastSlowWarn = DateTime.MinValue;

    // Anel do cursor (F-14): última posição conhecida p/ re-render em mudança
    // de estado; pens congelados cacheados por (cor, espessura) — zero alloc
    // por mouse-move no caminho estável.
    private Pt? _lastRingPos;
    private Pen? _ringPen;
    private Pen? _ringHaloPen;
    private Brush? _ringDotBrush;
    private Rgba _ringColorKey;
    private float _ringWidthKey = float.NaN;

    // Select (REQ1): marquee cria captura; arrasto sobre captura existente move.
    private bool _marqueeing;
    private Pt _marqueeAnchor;
    private ScreenObject? _movingScreen;
    private Pt _moveGrabOffset;
    private Pt _moveCurrent;
    private bool _capturing;

    // Texto como objeto: _movingText espelha _movingScreen (visual ao vivo,
    // modelo só no Up via MoveTextCommand). _lastTextTap detecta duplo-toque
    // (mouse e stylus pelo mesmo relógio — sem ClickCount).
    private TextObject? _movingText;
    private Pt _moveTextGrabOffset;
    private Pt _moveTextCurrent;
    private (int Id, DateTime At, Pt Pos) _lastTextTap;

    // Formas (genérico REQ8): um slot p/ Rectangle e Circle (IMovableShape).
    // Visual ao vivo via Offset; modelo commita no Up via MoveShapeCommand.
    private IMovableShape? _movingShape;
    private Pt _moveShapeGrabOffset;
    private Pt _moveShapeCurrent;

    // Texto: a OverlayWindow dona do TextBox coordena via estes membros.
    // IsEditingText = caixa aberta; CommitEditRequested = fecha e commita
    // (chamado no início de cada novo gesto — clique fora finaliza a edição).
    // TextEditRequested = clique com a ferramenta Texto (coords LOCAIS).
    public bool IsEditingText { get; set; }
    public Action? CommitEditRequested { get; set; }
    public event Action<Pt>? TextEditRequested;
    // Reedição de texto existente (duplo-toque com Select): a OverlayWindow
    // abre a caixa pré-preenchida; o commit atualiza o mesmo objeto.
    public event Action<TextObject>? TextReeditRequested;

    // Frame de coordenadas deste overlay: input local (DIP) → global (px da
    // tela virtual) na entrada; global → local na renderização. Documento
    // (AppState) vive sempre no espaço global, compartilhado entre overlays.
    // Default identidade: comportamento de monitor único inalterado.
    // NOTA escala: usa PxPerDipX (monitores reais têm Sx==Sy; par não-uniforme
    // é aproximação documentada — espessura preservada no eixo X).
    public MonitorFrame Frame { get; set; } = MonitorFrame.Identity;

    public IScreenCaptureFlow? CaptureFlow { get; set; }

    public InkSurface()
    {
        AddVisualChild(_hit);
        AddVisualChild(_screens);
        AddVisualChild(_finalized);
        AddVisualChild(_active);
        AddVisualChild(_selection);
        AddVisualChild(_cursorRing);
        Focusable = false;
    }

    protected override int VisualChildrenCount => 6;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _hit,
        1 => _screens,
        2 => _finalized,
        3 => _active,
        4 => _selection,
        _ => _cursorRing,
    };

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshHitRect();
    }

    // Redesenhado no Arrange (layout, sem precisar de composition pass).
    private void RefreshHitRect()
    {
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        using var dc = _hit.RenderOpen();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    public double ProcessingP50Ms
    {
        get
        {
            if (_sampleCount == 0) return 0;
            var copy = new double[Math.Min(_sampleCount, _samples.Length)];
            Array.Copy(_samples, copy, copy.Length);
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }
    }

    private AppState? _attached;

    public void Attach(AppState state)
    {
        Detach(); // idempotente: nunca assina 2× o mesmo estado
        _state = state;
        _attached = state;
        ClearVisuals(); // sync inicial: replay do documento (rebuild não apaga tinta)
        state.StrokeAdded += OnStrokeAdded;
        state.StrokesRemoved += OnStrokesRemoved;
        state.ScreenAdded += OnScreenAdded;
        state.ScreensRemoved += OnScreensRemoved;
        state.ScreenMoved += OnScreenMoved;
        state.TextAdded += OnTextAdded;
        state.TextsRemoved += OnTextsRemoved;
        state.TextMoved += RefreshTextVisual;
        state.TextChanged += RefreshTextVisual;
        state.RectangleAdded += OnRectangleAdded;
        state.RectanglesRemoved += OnRectanglesRemoved;
        state.RectangleMoved += RefreshRectangleVisual;
        state.CircleAdded += OnCircleAdded;
        state.CirclesRemoved += OnCirclesRemoved;
        state.CircleMoved += RefreshCircleVisual;
        state.StateChanged += RefreshSelection;
        foreach (var s in state.Strokes) OnStrokeAdded(s);
        foreach (var screen in state.Screens) OnScreenAdded(screen);
        foreach (var t in state.Texts) OnTextAdded(t);
        foreach (var r in state.Rectangles) OnRectangleAdded(r);
        foreach (var c in state.Circles) OnCircleAdded(c);
        RefreshSelection();
    }

    // Sync inicial full (construção/rebuild/re-attach): único lugar com
    // rebuild-all — o caminho incremental (Affected) segue nos eventos.
    private void ClearVisuals()
    {
        _finalized.Children.Clear();
        _map.Clear();
        _textVisuals.Clear();
        _rectVisuals.Clear();
        _circleVisuals.Clear();
        _screens.Children.Clear();
        _screenVisuals.Clear();
        _screenImages.Clear();
        ClearPreview();
    }

    // Rebuild de topologia fecha overlays com o AppState vivo: sem isso o
    // estado morto seguraria as superfícies (vazamento + renders fantasmas).
    // Pós-Detach a superfície não commita mais (_state nulo).
    public void Detach()
    {
        if (_attached is null) return;
        _attached.StrokeAdded -= OnStrokeAdded;
        _attached.StrokesRemoved -= OnStrokesRemoved;
        _attached.ScreenAdded -= OnScreenAdded;
        _attached.ScreensRemoved -= OnScreensRemoved;
        _attached.ScreenMoved -= OnScreenMoved;
        _attached.TextAdded -= OnTextAdded;
        _attached.TextsRemoved -= OnTextsRemoved;
        _attached.TextMoved -= RefreshTextVisual;
        _attached.TextChanged -= RefreshTextVisual;
        _attached.RectangleAdded -= OnRectangleAdded;
        _attached.RectanglesRemoved -= OnRectanglesRemoved;
        _attached.RectangleMoved -= RefreshRectangleVisual;
        _attached.CircleAdded -= OnCircleAdded;
        _attached.CirclesRemoved -= OnCirclesRemoved;
        _attached.CircleMoved -= RefreshCircleVisual;
        _attached.StateChanged -= RefreshSelection;
        _attached = null;
        _state = null;
    }

    private void OnStrokeAdded(Stroke s)
    {
        // Stroke em coords globais → visual na coords locais deste overlay
        // (pontos E largura: px globais ÷ escala local = DIP local).
        var visual = WpfStrokeRenderer.BuildStrokeVisual(
            Frame.ToLocalList(s.Points), s.Color, s.WidthPx / Frame.PxPerDipX, s.Tool, s.Opacity);
        _finalized.Children.Add(visual);
        _map[s.Id] = visual;
        ClearPreview();
    }

    private void OnStrokesRemoved(IReadOnlyList<Stroke> list)
    {
        foreach (var s in list)
            if (_map.Remove(s.Id, out var visual))
                _finalized.Children.Remove(visual);
        ClearPreview();
    }

    private void OnScreenAdded(ScreenObject screen)
    {
        var image = WpfStrokeRenderer.CreateBitmap(screen.Bgra, screen.PixelWidth, screen.PixelHeight);
        // Captura em px globais → posição/tamanho locais deste overlay.
        var local = Frame.ToLocal(screen.Bounds);
        var visual = WpfStrokeRenderer.BuildScreenVisual(image, local);
        _screens.Children.Add(visual);
        _screenVisuals[screen.Id] = visual;
        _screenImages[screen.Id] = image;
        ClearPreview();
    }

    private void OnScreensRemoved(IReadOnlyList<ScreenObject> list)
    {
        foreach (var s in list)
        {
            if (_screenVisuals.Remove(s.Id, out var visual))
                _screens.Children.Remove(visual);
            _screenImages.Remove(s.Id);
        }
        ClearPreview();
    }

    private void OnScreenMoved(ScreenObject screen)
    {
        if (_screenVisuals.TryGetValue(screen.Id, out var visual))
        {
            var local = Frame.ToLocal(new Pt(screen.X, screen.Y));
            visual.Offset = new Vector(local.X, local.Y);
        }
    }

    private void OnTextAdded(TextObject text)
    {
        // Texto em px globais → posição/tamanho locais deste overlay.
        var local = Frame.ToLocal(new Pt(text.X, text.Y));
        var visual = WpfStrokeRenderer.BuildTextVisual(local,
            text.FontSizePx / Frame.PxPerDipX, text.FontFamily, text.Color, text.Content,
            Frame.PxPerDipX);
        _finalized.Children.Add(visual);
        _textVisuals[text.Id] = visual;
    }

    private void OnTextsRemoved(IReadOnlyList<TextObject> list)
    {
        foreach (var t in list)
            if (_textVisuals.Remove(t.Id, out var visual))
                _finalized.Children.Remove(visual);
    }

    // Move/reedição: reconstrói o visual no estado atual do modelo (posição,
    // conteúdo ou tamanho novos; Offset do arrasto zerado junto).
    private void RefreshTextVisual(TextObject text)
    {
        if (_textVisuals.Remove(text.Id, out var old))
            _finalized.Children.Remove(old);
        OnTextAdded(text);
    }

    private void OnRectangleAdded(RectangleObject rect)
    {
        // Forma em px globais → visual local (geometria em 0,0 + Offset).
        var local = Frame.ToLocal(rect.Bounds);
        var visual = WpfStrokeRenderer.BuildRectangleVisual(local, rect.Color,
            rect.StrokeWidthPx / Frame.PxPerDipX);
        _finalized.Children.Add(visual);
        _rectVisuals[rect.Id] = visual;
        ClearPreview();
    }

    private void OnRectanglesRemoved(IReadOnlyList<RectangleObject> list)
    {
        foreach (var r in list)
            if (_rectVisuals.Remove(r.Id, out var visual))
                _finalized.Children.Remove(visual);
        ClearPreview();
    }

    // Move: reconstrói no estado atual do modelo (Offset do arrasto zerado).
    private void RefreshRectangleVisual(RectangleObject rect)
    {
        if (_rectVisuals.Remove(rect.Id, out var old))
            _finalized.Children.Remove(old);
        // Rebuild sem limpar o preview do gesto vizinho: OnRectangleAdded
        // limpa o preview (fim do ciclo de move/commit).
        var local = Frame.ToLocal(rect.Bounds);
        var visual = WpfStrokeRenderer.BuildRectangleVisual(local, rect.Color,
            rect.StrokeWidthPx / Frame.PxPerDipX);
        _finalized.Children.Add(visual);
        _rectVisuals[rect.Id] = visual;
        ClearPreview();
    }

    private void OnCircleAdded(CircleObject circle)
    {
        var local = Frame.ToLocal(circle.Bounds);
        var visual = WpfStrokeRenderer.BuildCircleVisual(local, circle.Color,
            circle.StrokeWidthPx / Frame.PxPerDipX);
        _finalized.Children.Add(visual);
        _circleVisuals[circle.Id] = visual;
        ClearPreview();
    }

    private void OnCirclesRemoved(IReadOnlyList<CircleObject> list)
    {
        foreach (var c in list)
            if (_circleVisuals.Remove(c.Id, out var visual))
                _finalized.Children.Remove(visual);
        ClearPreview();
    }

    private void RefreshCircleVisual(CircleObject circle)
    {
        if (_circleVisuals.Remove(circle.Id, out var old))
            _finalized.Children.Remove(old);
        var local = Frame.ToLocal(circle.Bounds);
        var visual = WpfStrokeRenderer.BuildCircleVisual(local, circle.Color,
            circle.StrokeWidthPx / Frame.PxPerDipX);
        _finalized.Children.Add(visual);
        _circleVisuals[circle.Id] = visual;
        ClearPreview();
    }

    // Caminho de teste headless: mesmo commit do gesto real, sem HWND/eventos.
    // Pontos em coords LOCAIS (como o gesto); commit converte p/ global.
    // Com Frame identidade (default), local == global (testes atuais intactos).
    public void SimulateStroke(List<Pt> points)
    {
        if (_state is null) return;
        var sw = Stopwatch.StartNew();
        var filtered = new List<Pt>(points.Count);
        float eps = StrokeSpec.CaptureMinDistance(_state.ActiveWidth);
        foreach (var p in points) InputFilter.TryAppend(filtered, p, eps);
        PreviewFreehand(filtered);
        _state.AddFreehand(Frame.ToGlobalList(filtered), _state.ActiveWidth * Frame.PxPerDipX);
        sw.Stop();
        PushSample(sw.Elapsed.TotalMilliseconds);
    }

    public void SimulateShape(ToolKind tool, Pt a, Pt b)
    {
        if (_state is null) return;
        if (tool is ToolKind.Rectangle or ToolKind.Circle)
        {
            PreviewShape(tool, a, b);
            CommitShape(tool, Frame.ToGlobal(a), Frame.ToGlobal(b));
            return;
        }
        PreviewShape(tool, a, b);
        _state.AddShape(tool, Frame.ToGlobal(a), Frame.ToGlobal(b),
            _state.Presets[tool].WidthDip * Frame.PxPerDipX);
    }

    public void SimulateErase(Pt a, Pt b) => _state?.EraseSegment(Frame.ToGlobal(a), Frame.ToGlobal(b));

    // Caminho de teste: clique com a ferramenta Texto (mesmo evento do gesto
    // real, sem HWND). Não cria stroke nem captura mouse.
    public void SimulateTextRequest(Pt local)
    {
        if (_state is null || _state.ActiveTool != ToolKind.Text) return;
        if (IsEditingText)
            CommitEditRequested?.Invoke();
        TextEditRequested?.Invoke(local);
    }

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        _lastStylusMs = InputDedup.NowMs();
        try
        {
            if (_state is null || !_state.IsDrawMode) return;
            if (IsNonLeftMouseClick()) return; // botão direito/meio não desenha
            BeginAt(ToPt(e.GetPosition(this)), "stylus", () => e.StylusDevice.Capture(this));
        }
        catch (Exception ex) { Log.Error("falha no início do traço", ex); }
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        _lastStylusMs = InputDedup.NowMs();
        try
        {
            var p = ToPt(e.GetPosition(this));
            UpdateCursorRing(p);
            if (_state is null) return;
            MoveAt(p);
        }
        catch (Exception ex) { Log.Error("falha durante o traço", ex); }
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        _lastStylusMs = InputDedup.NowMs();
        try { EndAt(ToPt(e.GetPosition(this))); }
        catch (Exception ex) { Log.Error("falha no commit do traço", ex); }
        if (e.StylusDevice.Captured == this) e.StylusDevice.Capture(null);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        try
        {
            if (_state is null || !_state.IsDrawMode) return;
            if (!InputDedup.ShouldAcceptMouse(_lastStylusMs, InputDedup.NowMs())) return; // promoção
            BeginAt(ToPt(e.GetPosition(this)), "mouse", () => Mouse.Capture(this));
        }
        catch (Exception ex) { Log.Error("falha no início do traço (mouse)", ex); }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        try
        {
            var p = ToPt(e.GetPosition(this));
            UpdateCursorRing(p); // hover: anel segue o mouse mesmo sem gesto
            if (_state is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!InputDedup.ShouldAcceptMouse(_lastStylusMs, InputDedup.NowMs())) return; // promoção
            MoveAt(p);
        }
        catch (Exception ex) { Log.Error("falha durante o traço (mouse)", ex); }
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateCursorRing(ToPt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _lastRingPos = null;
        using (_cursorRing.RenderOpen()) { } // sem seleção de posição: anel vazio
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        try
        {
            if (!InputDedup.ShouldAcceptMouse(_lastStylusMs, InputDedup.NowMs())) return; // promoção
            EndAt(ToPt(e.GetPosition(this)));
        }
        catch (Exception ex) { Log.Error("falha no commit do traço (mouse)", ex); }
        if (Mouse.Captured == this) Mouse.Capture(null);
        e.Handled = true;
    }

    private void BeginAt(Pt p, string src, Action capture)
    {
        if (_state is null) return;
        // Qualquer novo gesto finaliza antes a edição de texto pendente
        // (clique fora do campo commita; síncrono, sem mudar a ferramenta).
        if (IsEditingText)
            CommitEditRequested?.Invoke();
        // p = coords locais (DIP desta janela); g = espaço global do documento.
        Pt g = Frame.ToGlobal(p);
        Log.Info($"input begin tool={_state.ActiveTool} src={src} x={g.X:F0} y={g.Y:F0}");
        if (_state.ActiveTool == ToolKind.Text)
        {
            // Sem mouse capture de propósito: a caixa de edição (elemento irmão
            // na janela) precisa do mouse p/ caret/seleção de texto.
            TextEditRequested?.Invoke(p);
            return;
        }
        if (_state.ActiveTool == ToolKind.Select)
        {
            if (_capturing) return; // captura em voo: gesto ignorado (sem deadlock)
            HandleSelectTap(p, capture);
            return;
        }
        if (_state.ActiveTool == ToolKind.EraserStroke)
        {
            _erasing = true;
            _lastErase = g;
            _state.EraseSegment(g, g);
        }
        else if (_state.ActiveTool is ToolKind.Line or ToolKind.Arrow
            or ToolKind.Rectangle or ToolKind.Circle)
        {
            _shaping = true;
            _anchor = p;
            capture();
        }
        else
        {
            _drawing = true;
            _raw = new List<Pt>(256) { p };
            capture();
        }
    }

    // Toque com Select: UM hit-test centralizado (ObjectPicker: _finalized por
    // Id — textos/retângulos/círculos — acima das capturas). Objeto → seleciona
    // e prepara move; vazio → desseleciona e inicia marquee de REGIÃO (nunca
    // confunde região com objeto — REQ7). Nunca cria stroke (REQ8).
    private string HandleSelectTap(Pt p, Action capture)
    {
        if (_state is null) return "ignored";
        Pt g = Frame.ToGlobal(p);
        var pick = ObjectPicker.PickTopmost(
            _state.Texts, _state.Rectangles, _state.Circles, _state.Screens, g);
        if (pick is { Kind: CanvasObjectKind.Text } textPick
            && _state.Texts.FirstOrDefault(t => t.Id == textPick.Id) is { } hitText)
        {
            // Duplo-toque no mesmo texto (<500 ms, <8 px): reeditar em vez de
            // mover. Vale p/ mouse, caneta e touch (mesmo relógio).
            var now = DateTime.UtcNow;
            if (_lastTextTap.Id == hitText.Id &&
                (now - _lastTextTap.At).TotalMilliseconds < 500 &&
                _lastTextTap.Pos.DistanceTo(g) < 8)
            {
                _lastTextTap = default;
                TextReeditRequested?.Invoke(hitText);
                return "reedit";
            }
            _lastTextTap = (hitText.Id, now, g);
            _movingText = hitText;
            _moveTextGrabOffset = new Pt(g.X - hitText.X, g.Y - hitText.Y);
            _moveTextCurrent = new Pt(hitText.X, hitText.Y);
            _state.SelectObject(pick);
            capture();
            return "text";
        }
        _lastTextTap = default;
        if (pick is { Kind: CanvasObjectKind.Rectangle } rectPick
            && _state.Rectangles.FirstOrDefault(r => r.Id == rectPick.Id) is { } hitRect)
        {
            BeginMoveShape(hitRect, g, pick.Value, capture);
            return "rectangle";
        }
        if (pick is { Kind: CanvasObjectKind.Circle } circlePick
            && _state.Circles.FirstOrDefault(c => c.Id == circlePick.Id) is { } hitCircle)
        {
            BeginMoveShape(hitCircle, g, pick.Value, capture);
            return "circle";
        }
        if (pick is { Kind: CanvasObjectKind.Screen } screenPick
            && _state.Screens.FirstOrDefault(s => s.Id == screenPick.Id) is { } hit)
        {
            _movingScreen = hit;
            _moveGrabOffset = new Pt(g.X - hit.X, g.Y - hit.Y);
            _moveCurrent = new Pt(hit.X, hit.Y);
            _state.SelectObject(pick);
            capture();
            return "screen";
        }
        _marqueeing = true;
        _marqueeAnchor = p;
        _state.ClearSelection();
        capture();
        return "marquee";
    }

    private void BeginMoveShape(IMovableShape shape, Pt g, SelectedObject pick, Action capture)
    {
        _movingShape = shape;
        _moveShapeGrabOffset = new Pt(g.X - shape.X, g.Y - shape.Y);
        _moveShapeCurrent = new Pt(shape.X, shape.Y);
        _state?.SelectObject(pick);
        capture();
    }

    // Seams de teste headless (mesmo código do gesto real, sem HWND).
    public string SimulateSelectTap(Pt local)
    {
        if (_state is null || _state.ActiveTool != ToolKind.Select) return "ignored";
        if (IsEditingText)
            CommitEditRequested?.Invoke();
        return HandleSelectTap(local, () => { });
    }

    // Arrastar completo de texto: seleciona, arrasta ao vivo e commita no Up.
    public void SimulateTextDrag(Pt fromLocal, Pt toLocal)
    {
        if (_state is null || _state.ActiveTool != ToolKind.Select) return;
        SimulateSelectTap(fromLocal);
        MoveAt(toLocal);
        EndAt(toLocal);
    }

    // Arrastar genérico de qualquer objeto selecionável (texto/captura/
    // retângulo/círculo): mesmo gesto real, sem HWND.
    public void SimulateObjectDrag(Pt fromLocal, Pt toLocal)
    {
        if (_state is null || _state.ActiveTool != ToolKind.Select) return;
        SimulateSelectTap(fromLocal);
        MoveAt(toLocal);
        EndAt(toLocal);
    }

    // Drag de criação de forma (ferramenta Rectangle/Circle): Down→Move→Up.
    public void SimulateShapeDrag(Pt fromLocal, Pt toLocal)
    {
        if (_state is null) return;
        BeginAt(fromLocal, "test", () => { });
        MoveAt(toLocal);
        EndAt(toLocal);
    }

    private void MoveAt(Pt p)
    {
        if (_state is null) return;
        if (IsEditingText || _state.ActiveTool == ToolKind.Text) return; // texto: sem gesto
        if (_movingText is not null)
        {
            // Arrasto ao vivo move SÓ o visual (Offset delta, sem re-render);
            // o modelo commita no Up via MoveTextCommand (undo puro, sem spam).
            Pt gt = Frame.ToGlobal(p);
            _moveTextCurrent = new Pt(gt.X - _moveTextGrabOffset.X, gt.Y - _moveTextGrabOffset.Y);
            if (_textVisuals.TryGetValue(_movingText.Id, out var tvisual))
            {
                var tlocal = Frame.ToLocal(_moveTextCurrent);
                var origin = Frame.ToLocal(new Pt(_movingText.X, _movingText.Y));
                tvisual.Offset = new Vector(tlocal.X - origin.X, tlocal.Y - origin.Y);
            }
            // Highlight acompanha o visual (modelo só commita no Up).
            WpfStrokeRenderer.RenderMarquee(_selection, Frame.ToLocal(new RectD(
                _moveTextCurrent.X, _moveTextCurrent.Y, _movingText.WidthPx, _movingText.HeightPx)));
            return;
        }
        if (_movingScreen is not null)
        {
            // Arrasto ao vivo move SÓ o visual (Offset, sem re-render); o modelo
            // commita no Up via MoveScreenCommand (undo puro, sem spam).
            // Modelo em global; visual convertido p/ local deste overlay.
            Pt g = Frame.ToGlobal(p);
            _moveCurrent = new Pt(g.X - _moveGrabOffset.X, g.Y - _moveGrabOffset.Y);
            if (_screenVisuals.TryGetValue(_movingScreen.Id, out var visual))
            {
                var local = Frame.ToLocal(_moveCurrent);
                visual.Offset = new Vector(local.X, local.Y);
            }
            // Highlight acompanha o visual (modelo só commita no Up).
            WpfStrokeRenderer.RenderMarquee(_selection, Frame.ToLocal(new RectD(
                _moveCurrent.X, _moveCurrent.Y, _movingScreen.WidthPx, _movingScreen.HeightPx)));
            return;
        }
        if (_movingShape is not null)
        {
            // Mesmo padrão das demais formas: só o visual (Offset) + highlight;
            // o modelo commita no Up via MoveShapeCommand genérico (REQ8).
            Pt g = Frame.ToGlobal(p);
            _moveShapeCurrent = new Pt(g.X - _moveShapeGrabOffset.X, g.Y - _moveShapeGrabOffset.Y);
            DrawingVisual? svisual = _movingShape.Kind switch
            {
                CanvasObjectKind.Rectangle when _rectVisuals.TryGetValue(_movingShape.Id, out var rv) => rv,
                CanvasObjectKind.Circle when _circleVisuals.TryGetValue(_movingShape.Id, out var cv) => cv,
                _ => null,
            };
            if (svisual is not null)
            {
                var local = Frame.ToLocal(_moveShapeCurrent);
                svisual.Offset = new Vector(local.X, local.Y);
            }
            WpfStrokeRenderer.RenderMarquee(_selection,
                Frame.ToLocal(new RectD(_moveShapeCurrent.X, _moveShapeCurrent.Y,
                    _movingShape.Bounds.Width, _movingShape.Bounds.Height)));
            return;
        }
        if (_marqueeing)
        {
            WpfStrokeRenderer.RenderMarquee(_active, NormalizeMarquee(_marqueeAnchor, p));
            return;
        }
        if (_erasing)
        {
            Pt g = Frame.ToGlobal(p);
            _state.EraseSegment(_lastErase, g);
            _lastErase = g;
        }
        else if (_drawing)
        {
            var sw = Stopwatch.StartNew();
            if (InputFilter.TryAppend(_raw, p, StrokeSpec.CaptureMinDistance(_state.ActiveWidth)))
                PreviewFreehand(_raw);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            PushSample(ms);
            // Problema de latência vai para o log (com throttle; nunca por move).
            if (ms > 8 && (DateTime.Now - _lastSlowWarn).TotalSeconds > 5)
            {
                _lastSlowWarn = DateTime.Now;
                Log.Warn($"move lento: {ms:F1} ms (teto 8 ms, pts={_raw.Count})");
            }
        }
        else if (_shaping)
        {
            PreviewShape(_state.ActiveTool, _anchor, p);
        }
    }

    private void EndAt(Pt p)
    {
        if (_state is null) return;
        if (_state.ActiveTool == ToolKind.Text) return; // texto: sem gesto
        if (_movingText is not null && _state is not null)
        {
            // Sem mudança = MoveText recusa (sem comando, sem undo vazio).
            _state.MoveText(_movingText.Id, _moveTextCurrent.X, _moveTextCurrent.Y);
            _movingText = null;
        }
        if (_movingScreen is not null && _state is not null)
        {
            _state.MoveScreen(_movingScreen.Id, _moveCurrent.X, _moveCurrent.Y);
            _movingScreen = null;
        }
        if (_movingShape is not null && _state is not null)
        {
            // Sem mudança = MoveShape recusa (sem comando, sem undo vazio).
            _state.MoveShape(_movingShape.Kind, _movingShape.Id,
                _moveShapeCurrent.X, _moveShapeCurrent.Y);
            _movingShape = null;
        }
        if (_marqueeing)
        {
            _marqueeing = false;
            // Marquee local (DIP) → px globais (BitBlt trabalha em px físicos,
            // negativos OK p/ monitor à esquerda/acima).
            var rect = Frame.ToGlobal(NormalizeMarquee(_marqueeAnchor, p));
            // Preview do marquee fica até o commit limpar (ou cancela se mínimo).
            if (rect.Width >= 4 && rect.Height >= 4)
                _ = CommitMarqueeAsync(rect);
            else
                ClearPreview();
        }
        if (_drawing && _state is not null)
        {
            // Gesto local → commit global (multi-monitor: stroke nasce no espaço
            // compartilhado; captura do mouse entrega o resto do gesto aqui).
            // Largura: DIP local × escala = px físicos (tamanho real preservado
            // em qualquer monitor, independente do DPI de origem).
            _state.AddFreehand(Frame.ToGlobalList(_raw), _state.ActiveWidth * Frame.PxPerDipX);
            _raw = new List<Pt>();
        }
        if (_shaping && _state is not null)
        {
            if (_state.ActiveTool is ToolKind.Rectangle or ToolKind.Circle)
                CommitShape(_state.ActiveTool, Frame.ToGlobal(_anchor), Frame.ToGlobal(p));
            else
                _state.AddShape(_state.ActiveTool, Frame.ToGlobal(_anchor), Frame.ToGlobal(p),
                    _state.Presets[_state.ActiveTool].WidthDip * Frame.PxPerDipX);
        }
        _drawing = false;
        _erasing = false;
        _shaping = false;
    }

    // Commit de forma paramétrica (REQ2): normaliza o drag (qualquer direção),
    // converte p/ px globais e cria UM objeto (mínimo não atinge = cancelado,
    // sem undo). Preview some via evento de Added (ClearPreview).
    private void CommitShape(ToolKind tool, Pt a, Pt b)
    {
        if (_state is null) return;
        var bounds = ShapeGeometry.Normalize(a, b);
        var preset = _state.Presets[tool];
        float widthPx = preset.WidthDip * Frame.PxPerDipX;
        if (tool == ToolKind.Circle)
            _state.AddCircle(bounds, preset.Color, widthPx);
        else
            _state.AddRectangle(bounds, preset.Color, widthPx);
    }

    private async Task CommitMarqueeAsync(RectD rect)
    {
        if (_state is null || CaptureFlow is null || _capturing) return;
        _capturing = true;
        try
        {
            // rect já chega em px globais (chamador converteu o marquee local).
            var img = await CaptureFlow.CaptureRegionGlobalPxAsync(rect);
            if (img is null)
            {
                Log.Warn("marquee cancelado: captura falhou");
                return;
            }
            _state.AddScreen(img.Bgra, img.PixelWidth, img.PixelHeight, rect);
        }
        catch (Exception ex) { Log.Error("falha no commit da captura", ex); }
        finally { _capturing = false; ClearPreview(); }
    }

    // Caminho de teste: flow fake injeta bytes sintéticos (sem tela real).
    // Rect em coords LOCAIS (como o marquee); captura commita em global.
    public async Task SimulateMarqueeAsync(RectD rect)
    {
        if (_state is null || CaptureFlow is null) return;
        var global = Frame.ToGlobal(rect);
        var img = await CaptureFlow.CaptureRegionGlobalPxAsync(global);
        if (img is null) return;
        _state.AddScreen(img.Bgra, img.PixelWidth, img.PixelHeight, global);
    }

    private static RectD NormalizeMarquee(Pt a, Pt b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    // Feedback visual (REQ2): bounding box/outline tracejado sobre o objeto
    // selecionado, sem alterar sua aparência permanente. MOVE ao vivo junto
    // com o visual arrastado (MoveAt redesenha); aqui é o estado commitado.
    private void RefreshSelection()
    {
        RectD? bounds = _state?.SelectedObject switch
        {
            { Kind: CanvasObjectKind.Text } sel
                when _state.Texts.FirstOrDefault(t => t.Id == sel.Id) is { } tt
                => new RectD(tt.X, tt.Y, tt.WidthPx, tt.HeightPx),
            { Kind: CanvasObjectKind.Screen } sel
                when _state.Screens.FirstOrDefault(s => s.Id == sel.Id) is { } s
                => s.Bounds,
            { Kind: CanvasObjectKind.Rectangle } sel
                when _state.Rectangles.FirstOrDefault(r => r.Id == sel.Id) is { } r
                => r.Bounds,
            { Kind: CanvasObjectKind.Circle } sel
                when _state.Circles.FirstOrDefault(c => c.Id == sel.Id) is { } c
                => c.Bounds,
            _ => null,
        };
        if (bounds.HasValue)
            WpfStrokeRenderer.RenderMarquee(_selection, Frame.ToLocal(bounds.Value));
        else
        {
            using var dc = _selection.RenderOpen(); // sem seleção: highlight vazio
        }
        // Estado (modo/ferramenta/cor/espessura) mudou: anel reavalia na última
        // posição (some se virou interagir/Select; troca de cor/espessura ao vivo).
        if (_lastRingPos is { } pos) UpdateCursorRing(pos);
    }

    // Anel do cursor (F-14): raio = espessura/2 em DIP local, anel na cor ativa
    // + halo escuro (contraste em qualquer fundo). Sem timers: só renderiza em
    // evento de input ou mudança de estado. Some no modo interagir, tinta
    // oculta e nas ferramentas de navegação (Select/Text).
    private void UpdateCursorRing(Pt p)
    {
        if (_state is null || !_state.IsDrawMode || !_state.InkVisible
            || _state.ActiveTool is ToolKind.Select or ToolKind.Text)
        {
            _lastRingPos = null;
            using (_cursorRing.RenderOpen()) { }
            return;
        }
        _lastRingPos = p;
        var color = _state.ActiveColor;
        float width = _state.ActiveWidth;
        if (_ringPen is null || !color.Equals(_ringColorKey) || Math.Abs(width - _ringWidthKey) > 0.01f)
        {
            _ringColorKey = color;
            _ringWidthKey = width;
            var media = Color.FromRgb(color.R, color.G, color.B);
            var halo = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), 2.0);
            var ring = new Pen(new SolidColorBrush(media), 1.5);
            var dot = new SolidColorBrush(media);
            halo.Freeze(); ring.Freeze(); dot.Freeze();
            _ringHaloPen = halo;
            _ringPen = ring;
            _ringDotBrush = dot;
        }
        double r = Math.Max(3.0, width / 2.0);
        var center = new Point(p.X, p.Y);
        using var dc = _cursorRing.RenderOpen();
        dc.DrawEllipse(null, _ringHaloPen, center, r + 1.25, r + 1.25);
        dc.DrawEllipse(null, _ringPen, center, r, r);
        dc.DrawEllipse(_ringDotBrush, null, center, 1.0, 1.0);
    }

    private void ClearPreview()
    {
        using var dc = _active.RenderOpen(); // reabrir sem desenhar = preview vazio
    }

    private void PreviewFreehand(List<Pt> points)
    {
        if (_state is null) return;
        WpfStrokeRenderer.RenderPreview(_active, points, _state.ActiveColor,
            _state.ActiveWidth, _state.ActiveTool, StrokeSpec.DefaultOpacity(_state.ActiveTool));
    }

    private void PreviewShape(ToolKind tool, Pt a, Pt b)
    {
        if (_state is null) return;
        var preset = _state.Presets[tool];
        if (tool is ToolKind.Rectangle or ToolKind.Circle)
        {
            // Preview do contorno no rect normalizado (qualquer direção).
            var rect = ShapeGeometry.Normalize(a, b);
            WpfStrokeRenderer.RenderShapePreview(_active, tool, rect, preset.Color, preset.WidthDip);
            return;
        }
        var points = tool == ToolKind.Line
            ? ShapeBuilder.BuildLine(a, b)
            : ShapeBuilder.BuildArrow(a, b, preset.WidthDip);
        // Formas sempre opacas (ver AppState.AddShape).
        WpfStrokeRenderer.RenderPreview(_active, points, preset.Color, preset.WidthDip, tool, 1f);
    }

    private void PushSample(double ms)
    {
        _samples[_sampleCount % _samples.Length] = ms;
        _sampleCount++;
    }

    private static Pt ToPt(Point p) => new((float)p.X, (float)p.Y);

    // TabletDeviceType só tem Stylus/Touch: caneta e mouse promovido são
    // indistinguíveis por tipo. Regra pelos botões físicos: só ignora quando
    // direito/meio está pressionado SEM o esquerdo (caneta genuína tem todos soltos).
    private static bool IsNonLeftMouseClick() =>
        Mouse.LeftButton != MouseButtonState.Pressed &&
        (Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed);
}
