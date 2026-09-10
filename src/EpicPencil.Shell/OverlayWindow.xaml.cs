using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public partial class OverlayWindow : Window
{
    private readonly AppState _state;
    private IntPtr _hwnd;

    public OverlayWindow(AppState state)
    {
        InitializeComponent();
        _state = state;
        Surface.Attach(state);
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        state.StateChanged += ApplyState;
        // Sonda de diagnóstico: prova se QUALQUER input (stylus ou mouse)
        // chega à janela, antes mesmo da superfície. Só Downs (baixa frequência).
        AddHandler(PreviewStylusDownEvent, new StylusDownEventHandler((s, e) =>
            Log.Info($"janela PreviewStylusDown x={e.GetPosition(this).X:F0} y={e.GetPosition(this).Y:F0}")),
            handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) =>
            Log.Info($"janela PreviewMouseDown btn={e.ChangedButton} x={e.GetPosition(this).X:F0} y={e.GetPosition(this).Y:F0}")),
            handledEventsToo: true);
    }

    public InkSurface SurfaceControl => Surface;
    public IntPtr Handle => _hwnd;

    // Diagnóstico: pinta tudo de vermelho por 400 ms. Se o usuário NÃO vir
    // vermelho, a janela está ausente/coberta (z-order/visibilidade), não é input.
    public void FlashTest()
    {
        FlashPanel.Visibility = Visibility.Visible;
        Log.Info("flash ON (tela deve ficar vermelha 400ms)");
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FlashPanel.Visibility = Visibility.Collapsed;
            Log.Info("flash OFF");
        };
        timer.Start();
    }

    public void ReassertFront()
    {
        if (_hwnd == IntPtr.Zero) return;
        OverlayBehavior.ReassertTopmost(_hwnd);
        Log.Info($"reassert topmost manual: {OverlayBehavior.Describe(_hwnd)}");
    }

    public string DiagnosePoint(IntPtr toolbarHwnd)
    {
        if (_hwnd == IntPtr.Zero || !OverlayBehavior.GetCursor(out int x, out int y))
            return "sem HWND/cursor";
        string desc = OverlayBehavior.DescribePointOwner(x, y, _hwnd, toolbarHwnd);
        Log.Info($"diagnóstico ponto ({x},{y}): {desc}");
        Log.Info("  " + OverlayBehavior.ProbeHitTest(_hwnd, x, y));
        foreach (var line in OverlayBehavior.DescribeZOrder(_hwnd, toolbarHwnd, 14))
            Log.Info("  " + line);
        return desc;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        OverlayBehavior.ApplyBaseStyles(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndHook);
        ApplyState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Fullscreen manual (WPF proíbe ShowActivated=False + Maximized).
        // Multi-monitor (1 janela por monitor) entra no S4; aqui: monitor primário.
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Log.Info($"overlay bounds={Width:F0}x{Height:F0} monitores={OverlayBehavior.MonitorCount()} " +
            $"topmost={Topmost} {OverlayBehavior.Describe(_hwnd)} " +
            $"tier={System.Windows.Media.RenderCapability.Tier >> 16}");
        if (OverlayBehavior.MonitorCount() > 1)
            Log.Warn(">1 monitor: overlay cobre só o primário (S4 pendente) — desenhe no monitor principal");
    }

    private void ApplyState()
    {
        if (_hwnd == IntPtr.Zero) { Log.Warn("ApplyState sem HWND (janela ainda não criada)"); return; }
        OverlayBehavior.SetClickThrough(_hwnd, !_state.IsDrawMode);
        Surface.Visibility = _state.InkVisible ? Visibility.Visible : Visibility.Hidden;
        ModeBadge.Fill = _state.IsDrawMode ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.OrangeRed;
        ModeBadge.ToolTip = _state.IsDrawMode ? "Modo desenho (clique e arraste para riscar)"
            : "Modo interagir (cliques atravessam — volte pela toolbar)";
        Log.Info($"overlay aplicado modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"clickThrough={OverlayBehavior.IsClickThrough(_hwnd)} tinta={_state.InkVisible}");
    }

    private static IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (OverlayBehavior.TryHandleMouseActivate(msg, out var result))
        {
            handled = true;
            return result;
        }
        return IntPtr.Zero;
    }
}
