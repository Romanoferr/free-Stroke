// Superfície de tinta: hospeda os visuals + captura stylus/mouse.
// Decisões não óbvias:
//  - Só eventos Stylus*: o WPF promove mouse pelo stack de stylus, então tratar
//    Mouse* junto desenharia dobrado. StylusPlugIn/coalescing dedicado entra no S2.
//  - Preview reconstrói a geometria inteira do stroke ativo por move: O(n) por
//    move é aceitável porque o InputFilter mantém n em centenas (medido no p50).

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EpicPencil.Core;

namespace EpicPencil.Shell;

public sealed class InkSurface : FrameworkElement
{
    private readonly ContainerVisual _finalized = new();
    private readonly DrawingVisual _active = new();
    private readonly Dictionary<int, DrawingVisual> _map = new();
    private readonly double[] _samples = new double[128];
    private int _sampleCount;

    private AppState? _state;
    private List<Pt> _raw = new();
    private bool _drawing;
    private bool _erasing;
    private Pt _lastErase;

    public InkSurface()
    {
        AddVisualChild(_finalized);
        AddVisualChild(_active);
        Focusable = false;
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _finalized : _active;

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
        float eps = StrokeSpec.CaptureMinDistance(_state.PenWidth);
        foreach (var p in points) InputFilter.TryAppend(filtered, p, eps);
        WpfStrokeRenderer.RenderPreview(_active, filtered, _state.PenColor,
            _state.PenWidth, _state.ActiveTool, StrokeSpec.DefaultOpacity(_state.ActiveTool));
        _state.AddFreehand(filtered);
        sw.Stop();
        PushSample(sw.Elapsed.TotalMilliseconds);
    }

    public void SimulateErase(Pt a, Pt b) => _state?.EraseSegment(a, b);

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        if (_state is null || !_state.IsDrawMode) return;
        if (IsNonLeftMouseClick()) return; // botão direito/meio não desenha
        var p = ToPt(e.GetPosition(this));
        if (_state.ActiveTool == ToolKind.EraserStroke)
        {
            _erasing = true;
            _lastErase = p;
            _state.EraseSegment(p, p);
        }
        else
        {
            _drawing = true;
            _raw = new List<Pt>(256) { p };
            e.StylusDevice.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        if (_state is null) return;
        var p = ToPt(e.GetPosition(this));
        if (_erasing)
        {
            _state.EraseSegment(_lastErase, p);
            _lastErase = p;
        }
        else if (_drawing)
        {
            var sw = Stopwatch.StartNew();
            if (InputFilter.TryAppend(_raw, p, StrokeSpec.CaptureMinDistance(_state.PenWidth)))
                WpfStrokeRenderer.RenderPreview(_active, _raw, _state.PenColor,
                    _state.PenWidth, _state.ActiveTool, StrokeSpec.DefaultOpacity(_state.ActiveTool));
            sw.Stop();
            PushSample(sw.Elapsed.TotalMilliseconds);
        }
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        if (_drawing && _state is not null)
        {
            _state.AddFreehand(_raw);
            _raw = new List<Pt>();
        }
        _drawing = false;
        _erasing = false;
        if (e.StylusDevice.Captured == this) e.StylusDevice.Capture(null);
        e.Handled = true;
    }

    private void ClearPreview()
    {
        using var dc = _active.RenderOpen(); // reabrir sem desenhar = preview vazio
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
