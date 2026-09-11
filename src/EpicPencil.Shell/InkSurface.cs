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
    private readonly Dictionary<int, DrawingVisual> _screenVisuals = new();
    private readonly Dictionary<int, ImageSource> _screenImages = new();
    private readonly Dictionary<int, DrawingVisual> _map = new();
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

    // Select (REQ1): marquee cria captura; arrasto sobre captura existente move.
    private bool _marqueeing;
    private Pt _marqueeAnchor;
    private ScreenObject? _movingScreen;
    private Pt _moveGrabOffset;
    private Pt _moveCurrent;
    private bool _capturing;

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
        Focusable = false;
    }

    protected override int VisualChildrenCount => 5;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _hit,
        1 => _screens,
        2 => _finalized,
        3 => _active,
        _ => _selection,
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
        state.StateChanged += RefreshSelection;
        foreach (var s in state.Strokes) OnStrokeAdded(s);
        foreach (var screen in state.Screens) OnScreenAdded(screen);
        RefreshSelection();
    }

    // Sync inicial full (construção/rebuild/re-attach): único lugar com
    // rebuild-all — o caminho incremental (Affected) segue nos eventos.
    private void ClearVisuals()
    {
        _finalized.Children.Clear();
        _map.Clear();
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
        PreviewShape(tool, a, b);
        _state.AddShape(tool, Frame.ToGlobal(a), Frame.ToGlobal(b),
            _state.Presets[tool].WidthDip * Frame.PxPerDipX);
    }

    public void SimulateErase(Pt a, Pt b) => _state?.EraseSegment(Frame.ToGlobal(a), Frame.ToGlobal(b));

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
            if (_state is null) return;
            MoveAt(ToPt(e.GetPosition(this)));
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
            if (_state is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!InputDedup.ShouldAcceptMouse(_lastStylusMs, InputDedup.NowMs())) return; // promoção
            MoveAt(ToPt(e.GetPosition(this)));
        }
        catch (Exception ex) { Log.Error("falha durante o traço (mouse)", ex); }
        e.Handled = true;
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
        // p = coords locais (DIP desta janela); g = espaço global do documento.
        Pt g = Frame.ToGlobal(p);
        Log.Info($"input begin tool={_state.ActiveTool} src={src} x={g.X:F0} y={g.Y:F0}");
        if (_state.ActiveTool == ToolKind.Select)
        {
            if (_capturing) return; // captura em voo: gesto ignorado (sem deadlock)
            var hit = PickScreen(g);
            if (hit is not null)
            {
                _movingScreen = hit;
                _moveGrabOffset = new Pt(g.X - hit.X, g.Y - hit.Y);
                _moveCurrent = new Pt(hit.X, hit.Y);
                _state.SelectScreen(hit.Id);
                capture();
            }
            else
            {
                _marqueeing = true;
                _marqueeAnchor = p;
                _state.SelectScreen(null);
                capture();
            }
            return;
        }
        if (_state.ActiveTool == ToolKind.EraserStroke)
        {
            _erasing = true;
            _lastErase = g;
            _state.EraseSegment(g, g);
        }
        else if (_state.ActiveTool is ToolKind.Line or ToolKind.Arrow)
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

    private void MoveAt(Pt p)
    {
        if (_state is null) return;
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
        if (_movingScreen is not null && _state is not null)
        {
            _state.MoveScreen(_movingScreen.Id, _moveCurrent.X, _moveCurrent.Y);
            _movingScreen = null;
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
            _state.AddShape(_state.ActiveTool, Frame.ToGlobal(_anchor), Frame.ToGlobal(p),
                _state.Presets[_state.ActiveTool].WidthDip * Frame.PxPerDipX);
        _drawing = false;
        _erasing = false;
        _shaping = false;
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

    private ScreenObject? PickScreen(Pt p)
    {
        if (_state is null) return null;
        var all = _state.Screens;
        for (int i = all.Count - 1; i >= 0; i--) // topo primeiro (z-order)
            if (all[i].Contains(p)) return all[i];
        return null;
    }

    private static RectD NormalizeMarquee(Pt a, Pt b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private void RefreshSelection()
    {
        if (_state?.SelectedScreenId is int id
            && _state.Screens.FirstOrDefault(s => s.Id == id) is { } s)
            WpfStrokeRenderer.RenderMarquee(_selection, Frame.ToLocal(s.Bounds)); // mesmo tracejado do marquee
        else
        {
            using var dc = _selection.RenderOpen(); // sem seleção: highlight vazio
        }
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
