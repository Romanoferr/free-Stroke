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
    private readonly ContainerVisual _finalized = new();
    private readonly DrawingVisual _active = new();
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

    public InkSurface()
    {
        AddVisualChild(_hit);
        AddVisualChild(_finalized);
        AddVisualChild(_active);
        Focusable = false;
    }

    protected override int VisualChildrenCount => 3;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _hit,
        1 => _finalized,
        _ => _active,
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

    public void Attach(AppState state)
    {
        _state = state;
        state.StrokeAdded += s =>
        {
            var visual = WpfStrokeRenderer.BuildStrokeVisual(s);
            _finalized.Children.Add(visual);
            _map[s.Id] = visual;
            ClearPreview();
        };
        state.StrokesRemoved += list =>
        {
            foreach (var s in list)
                if (_map.Remove(s.Id, out var visual))
                    _finalized.Children.Remove(visual);
            ClearPreview();
        };
    }

    // Caminho de teste headless: mesmo commit do gesto real, sem HWND/eventos.
    public void SimulateStroke(List<Pt> points)
    {
        if (_state is null) return;
        var sw = Stopwatch.StartNew();
        var filtered = new List<Pt>(points.Count);
        float eps = StrokeSpec.CaptureMinDistance(_state.ActiveWidth);
        foreach (var p in points) InputFilter.TryAppend(filtered, p, eps);
        PreviewFreehand(filtered);
        _state.AddFreehand(filtered);
        sw.Stop();
        PushSample(sw.Elapsed.TotalMilliseconds);
    }

    public void SimulateShape(ToolKind tool, Pt a, Pt b)
    {
        if (_state is null) return;
        PreviewShape(tool, a, b);
        _state.AddShape(tool, a, b);
    }

    public void SimulateErase(Pt a, Pt b) => _state?.EraseSegment(a, b);

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
        Log.Info($"input begin tool={_state.ActiveTool} src={src} x={p.X:F0} y={p.Y:F0}");
        if (_state.ActiveTool == ToolKind.EraserStroke)
        {
            _erasing = true;
            _lastErase = p;
            _state.EraseSegment(p, p);
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
        if (_erasing)
        {
            _state.EraseSegment(_lastErase, p);
            _lastErase = p;
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
        if (_drawing && _state is not null)
        {
            _state.AddFreehand(_raw);
            _raw = new List<Pt>();
        }
        if (_shaping && _state is not null)
            _state.AddShape(_state.ActiveTool, _anchor, p);
        _drawing = false;
        _erasing = false;
        _shaping = false;
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
