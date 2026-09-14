using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public partial class OverlayWindow : Window
{
    private readonly AppState _state;
    private readonly MonitorInfo _monitor;
    private IntPtr _hwnd;
    private HwndSource? _source;

    // Sessão de edição de texto (ferramenta Texto): o TextBox vive nesta janela
    // (elemento irmão da superfície — nunca um visual permanente do canvas).
    // O overlay nasce NOACTIVATE, mas keystrokes exigem janela ativa: a sessão
    // limpa o bit, ativa, foca a caixa e restaura tudo ao fechar (best-effort).
    private bool _editingText;
    private Pt _editLocal;
    private float _editDip;
    private Rgba _editColor;
    private IntPtr _prevForeground;

    // Topologia mudou (plug/unplug/resolução/escala/primário): o App reconstrói
    // os overlays com debounce. Evento de instância (App assina cada overlay).
    public event Action? TopologyChanged;

    // Suprime o shutdown durante rebuild (fechamento programado ≠ usuário).
    public bool SuppressCloseShutdown { get; set; }

    public OverlayWindow(AppState state, MonitorInfo monitor)
    {
        InitializeComponent();
        _state = state;
        _monitor = monitor;
        Surface.Frame = monitor.Frame;
        Surface.Attach(state);
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        // REQ2: fechar QUALQUER janela = encerrar tudo (ShutdownMode explícito no App).
        // Exceção: fechamento programado do rebuild de topologia (SuppressCloseShutdown).
        Closed += (_, _) =>
        {
            CancelEdit(); // janela fechando: descarta rascunho sem commitar
            _source?.RemoveHook(WndHook);
            _source = null;
            // O AppState sobrevive ao rebuild: solta as assinaturas p/ a
            // superfície fechada não vazar nem renderizar fantasmas.
            _state.StateChanged -= ApplyState;
            Surface.Detach();
            if (SuppressCloseShutdown)
            {
                Log.Info($"overlay mon={_monitor.Id} fechado p/ rebuild (sem shutdown)");
                return;
            }
            Log.Info("overlay fechado → shutdown completo");
            Application.Current.Shutdown();
        };
        state.StateChanged += ApplyState;
        // Sonda de diagnóstico: prova se QUALQUER input (stylus ou mouse)
        // chega à janela, antes mesmo da superfície. Só Downs (baixa frequência).
        AddHandler(PreviewStylusDownEvent, new StylusDownEventHandler((s, e) =>
            Log.Info($"janela PreviewStylusDown x={e.GetPosition(this).X:F0} y={e.GetPosition(this).Y:F0}")),
            handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) =>
            Log.Info($"janela PreviewMouseDown btn={e.ChangedButton} x={e.GetPosition(this).X:F0} y={e.GetPosition(this).Y:F0}")),
            handledEventsToo: true);
        Surface.TextEditRequested += OnTextEditRequested;
        Surface.CommitEditRequested = CommitEdit;
        TextEditor.PreviewKeyDown += OnEditorKey;
        TextEditor.LostKeyboardFocus += (_, _) => CommitEdit();
        // Re-clique na caixa (ex. Alt+Tab no meio da edição): garante foreground.
        TextEditor.PreviewMouseLeftButtonDown += (_, _) => Activate();
    }

    public InkSurface SurfaceControl => Surface;
    public IntPtr Handle => _hwnd;
    public MonitorInfo Monitor => _monitor;

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

    // Abre a caixa exatamente no ponto clicado (DIPs locais). Fonte/cor/
    // tamanho congelados do AppState: textos futuros usam os novos valores,
    // os existentes nunca mudam. Caixa nativa só durante a edição.
    private void OnTextEditRequested(Pt local)
    {
        CommitEdit(); // segurança: nunca duas caixas (BeginAt já commitou)
        if (_hwnd == IntPtr.Zero) return;
        _editLocal = local;
        _editDip = _state.ActiveFontSizeDip;
        _editColor = _state.ActiveColor;
        TextEditor.FontFamily = TextFonts.Family;
        TextEditor.FontSize = _editDip;
        TextEditor.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(_editColor.R, _editColor.G, _editColor.B));
        TextEditor.Text = string.Empty;
        TextEditor.Margin = new Thickness(local.X, local.Y, 0, 0);
        TextEditor.Visibility = Visibility.Visible;
        Surface.IsEditingText = true;
        Surface.CommitEditRequested = CommitEdit;
        _prevForeground = OverlayBehavior.SaveForeground();
        OverlayBehavior.SetActivatable(_hwnd, true);
        Activate();
        TextEditor.Focus();
        _editingText = true;
        Log.Info($"edição texto aberta local=({local.X:F0},{local.Y:F0}) fonte={_editDip:F0}dip " +
            $"fg={OverlayBehavior.Describe(_hwnd)}");
    }

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        // Enter commita, Escape cancela. TODO o resto (letras de atalho,
        // Ctrl+C/X/V/Z nativos) pertence à caixa — nunca roteado p/ o app.
        if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
    }

    // Commit: fecha a caixa e cria o TextObject (posição/tamanho em px globais
    // na escala deste monitor). Vazio = descarta sem objeto e sem undo.
    private void CommitEdit()
    {
        if (!_editingText) return;
        _editingText = false;
        string text = TextEditor.Text;
        float dip = _editDip;
        Rgba color = _editColor;
        Pt local = _editLocal;
        HideEditorChrome();
        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Info("texto vazio descartado (sem objeto, sem undo)");
            return;
        }
        var g = Surface.Frame.ToGlobal(local);
        _state.AddText(text, g.X, g.Y, dip * Surface.Frame.PxPerDipX,
            TextFonts.PreferredFamily, color);
    }

    private void CancelEdit()
    {
        if (!_editingText) return;
        _editingText = false;
        HideEditorChrome();
        Log.Info("edição de texto cancelada (Escape)");
    }

    private void HideEditorChrome()
    {
        TextEditor.Visibility = Visibility.Collapsed;
        Surface.IsEditingText = false;
        Surface.CommitEditRequested = null;
        if (_hwnd != IntPtr.Zero)
        {
            OverlayBehavior.SetActivatable(_hwnd, false);
            if (!OverlayBehavior.RestoreForeground(_prevForeground))
                Log.Info("foreground anterior não restaurado (OS recusou; Alt+Tab p/ voltar)");
        }
        _prevForeground = IntPtr.Zero;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        OverlayBehavior.ApplyBaseStyles(_hwnd);
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndHook);
        ApplyState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Área de DESENHO = WORK AREA DO PRÓPRIO MONITOR (tela menos taskbar).
        // Regra de regiões (REQ3): a taskbar é área de INTERAÇÃO do shell — o
        // overlay nunca a cobre. Posicionamento em 2 passos: WPF (DIP aprox.)
        // + SetWindowPos físico (exato, imune a DPI misto).
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        // Escala real da janela (pode divergir do GetDpiForMonitor em frações).
        Surface.Frame = new EpicPencil.Core.MonitorFrame(_monitor.WorkX, _monitor.WorkY, (float)sx, (float)sy);
        Left = _monitor.WorkX / sx; Top = _monitor.WorkY / sy;
        Width = _monitor.WorkW / sx; Height = _monitor.WorkH / sy;
        if (_hwnd != IntPtr.Zero)
            OverlayBehavior.PlaceAt(_hwnd, _monitor.WorkX, _monitor.WorkY, _monitor.WorkW, _monitor.WorkH);
        Log.Info($"overlay mon={_monitor.Id} dev={_monitor.DeviceName} prim={_monitor.IsPrimary} " +
            $"work={_monitor.WorkW}x{_monitor.WorkH}@({_monitor.WorkX},{_monitor.WorkY})px " +
            $"dpi={sx:F2}x{sy:F2} bounds={Width:F0}x{Height:F0}@{Left:F0},{Top:F0} " +
            $"topmost={Topmost} {OverlayBehavior.Describe(_hwnd)} " +
            $"tier={System.Windows.Media.RenderCapability.Tier >> 16}");
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
        Log.Info($"overlay mon={_monitor.Id} aplicado modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"clickThrough={OverlayBehavior.IsClickThrough(_hwnd)} tinta={_state.InkVisible}");
    }

    private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (OverlayBehavior.TryHandleMouseActivate(msg, out var result))
        {
            handled = true;
            return result;
        }
        if (OverlayBehavior.IsDisplayChange(msg))
        {
            Log.Info($"overlay mon={_monitor.Id} recebeu WM_DISPLAYCHANGE → rebuild");
            TopologyChanged?.Invoke();
        }
        return IntPtr.Zero;
    }
}
