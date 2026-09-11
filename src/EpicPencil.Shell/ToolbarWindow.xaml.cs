// Toolbar mínima (semente da toolbar final): todos os comandos do incremento
// atual + atalhos LOCAIS (a overlay nunca tem foco, então as teclas vivem aqui).

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public partial class ToolbarWindow : Window
{
    // Paleta fixa do MVP: cobre fundo claro e escuro sem picker custom.
    private static readonly Rgba[] Palette =
    {
        new(0, 0, 0), new(255, 255, 255), new(255, 0, 0),
        new(0, 120, 215), new(0, 180, 0), new(255, 235, 59),
    };

    private readonly AppState _state;
    private IReadOnlyList<OverlayWindow> _overlays;
    private OverlayWindow _primary;
    private readonly List<Button> _swatches = new();

    // Selected visual (aplicado em código; hover/pressed vivem no XAML).
    private static readonly SolidColorBrush SelBg = CreateFrozen(0x1F, 0x6F, 0xB2);
    private static readonly SolidColorBrush SelBorder = CreateFrozen(0x6C, 0xB8, 0xF0);
    private readonly SolidColorBrush _collapsedColorBrush = new(Color.FromRgb(255, 0, 0));

    private static readonly Dictionary<ToolKind, string> ShortToolName = new()
    {
        [ToolKind.Pen] = "Caneta",
        [ToolKind.Pencil] = "Lápis",
        [ToolKind.Highlighter] = "Marca",
        [ToolKind.Line] = "Linha",
        [ToolKind.Arrow] = "Seta",
        [ToolKind.Select] = "Seleção",
        [ToolKind.EraserStroke] = "Borracha",
    };

    // Animação de collapse (130 ms, ease-out). Só apresentação; headless nunca alterna.
    private bool _collapsed;
    private bool _animating;
    private bool _syncingSlider; // RefreshStatus → slider sem reentrância
    private double _expandedW = 380;
    private double _expandedH = 320;
    private System.Windows.Threading.DispatcherTimer? _animTimer;

    // Self-test fecha janelas sem encerrar o processo (produção: fechar = sair).
    public bool SuppressCloseShutdown { get; set; }
    public bool IsCollapsed => _collapsed;

    public ToolbarWindow(AppState state, IReadOnlyList<OverlayWindow> overlays, OverlayWindow primary)
    {
        InitializeComponent();
        _state = state;
        _overlays = overlays;
        _primary = primary;
        // Assinado aqui (não no XAML): o Value inicial dispara ValueChanged
        // durante InitializeComponent, antes de _state existir (NRE → hang).
        ThicknessSlider.ValueChanged += OnThicknessChanged;
        foreach (var color in Palette)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                ToolTip = $"Cor #{color.R:X2}{color.G:X2}{color.B:X2}",
                Tag = color,
                Content = new System.Windows.Shapes.Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                },
            };
            swatch.Click += (_, _) => { _state.ActiveColor = color; RefreshStatus(); };
            ColorRow.Children.Add(swatch);
            _swatches.Add(swatch);
        }
        state.StateChanged += RefreshStatus;
        PreviewKeyDown += OnKey;
        // REQ2: fechar a toolbar encerra o processo (não minimiza para o nada —
        // não há tray nesta versão; fechamento = encerramento completo).
        // Exceção: self-test (SuppressCloseShutdown) fecha sem desligar.
        Closed += (_, _) =>
        {
            if (SuppressCloseShutdown) return;
            Log.Info("toolbar fechada → shutdown completo");
            Application.Current.Shutdown();
        };
        ToolTip = $"Log: {Log.Path}"; // onde debugar o que aconteceu
        HeaderGrip.PreviewMouseLeftButtonDown += (_, _) => TryDrag();
        CollapsedGrip.PreviewMouseLeftButtonDown += (_, _) => TryDrag();
        Loaded += (_, _) =>
        {
            // Congela o tamanho expandido: animação de collapse mexe Width/Height.
            _expandedW = ActualWidth;
            _expandedH = ActualHeight;
            SizeToContent = SizeToContent.Manual;
            Width = _expandedW;
            Height = _expandedH;
        };
        RefreshStatus();
    }

    // Rebuild de topologia troca a lista de overlays sem recriar a toolbar
    // (toolbar é única e compartilhada — nunca duplicada por monitor).
    public void Retarget(IReadOnlyList<OverlayWindow> overlays, OverlayWindow primary)
    {
        _overlays = overlays;
        _primary = primary;
        RefreshStatus();
    }

    private void OnToolPen(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Pen);
    private void OnToolPencil(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Pencil);
    private void OnToolMarker(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Highlighter);
    private void OnToolLine(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Line);
    private void OnToolArrow(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Arrow);
    private void OnToolSelect(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Select);
    private void OnDeleteScreen(object sender, RoutedEventArgs e) => _state.DeleteSelectedScreen();
    private void OnToolEraser(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.EraserStroke);
    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSlider) return;
        _state.ActiveWidth = (float)Math.Round(e.NewValue);
        RefreshStatus(); // ActiveWidth não dispara StateChanged (só presets)
    }
    private void OnUndo(object sender, RoutedEventArgs e) => _state.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _state.Redo();
    private void OnClear(object sender, RoutedEventArgs e) => _state.Clear();
    // Mostrar/ocultar unificado: visível = modo desenho; oculto = interagir
    // (click-through). Substitui o antigo par Desenhar/Interagir + Esconder.
    private void OnToggleInkMode(object sender, RoutedEventArgs e) => ToggleInkMode();
    private void ToggleInkMode()
    {
        if (_state.InkVisible)
        {
            _state.SetInkVisible(false);
            _state.SetDrawMode(false);
            Log.Info("desenho oculto → modo interagir");
        }
        else
        {
            _state.SetInkVisible(true);
            _state.SetDrawMode(true);
            Log.Info("desenho visível → modo desenho");
        }
    }

    private void HideInk()
    {
        if (!_state.InkVisible && !_state.IsDrawMode) return;
        _state.SetInkVisible(false);
        _state.SetDrawMode(false);
        Log.Info("desenho oculto → modo interagir");
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Log.Info("saída via botão Sair → shutdown completo");
        Application.Current.Shutdown();
    }
    private void OnFlash(object sender, RoutedEventArgs e)
    {
        foreach (var overlay in _overlays) overlay.FlashTest();
    }
    private void OnWho(object sender, RoutedEventArgs e)
    {
        // Amostragem com atraso: o cursor precisa estar SOBRE O CANVAS, não no botão.
        MessageBox.Show("Leve o mouse até o MEIO DA TELA (canvas, longe da toolbar) e aguarde 3 segundos SEM clicar.",
            "Diagnóstico em 3s");
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            IntPtr toolbarHwnd = new WindowInteropHelper(this).Handle;
            MessageBox.Show(_primary.DiagnosePoint(toolbarHwnd), "Quem recebe o clique?");
        };
        timer.Start();
    }
    private void OnFront(object sender, RoutedEventArgs e)
    {
        foreach (var overlay in _overlays) overlay.ReassertFront();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P) _state.SetTool(ToolKind.Pen);
        else if (e.Key == Key.B) _state.SetTool(ToolKind.Pencil);
        else if (e.Key == Key.H && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.SetTool(ToolKind.Highlighter);
        else if (e.Key == Key.L) _state.SetTool(ToolKind.Line);
        else if (e.Key == Key.S) _state.SetTool(ToolKind.Arrow);
        else if (e.Key == Key.V) _state.SetTool(ToolKind.Select);
        else if (e.Key == Key.Delete) _state.DeleteSelectedScreen();
        else if (e.Key == Key.E) _state.SetTool(ToolKind.EraserStroke);
        else if (e.Key == Key.D1) _state.SetActiveWidthPreset(0);
        else if (e.Key == Key.D2) _state.SetActiveWidthPreset(1);
        else if (e.Key == Key.D3) _state.SetActiveWidthPreset(2);
        else if (e.Key == Key.C) _state.Clear();
        else if (e.Key == Key.F9) ToggleInkMode(); // local: sem conflito global
        else if (e.Key == Key.PageUp) ToggleInkMode(); // funciona recolhida (mesma janela)
        else if (e.Key == Key.OemQuotes) ToggleInkMode(); // (') alternativa ao PgUp
        else if (e.Key == Key.Escape) HideInk(); // pânico local: oculta e solta o mouse
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Redo();
        else return;
        e.Handled = true;
    }

    private void RefreshStatus()
    {
        MarkSelected(PenButton, _state.ActiveTool == ToolKind.Pen);
        MarkSelected(PencilButton, _state.ActiveTool == ToolKind.Pencil);
        MarkSelected(MarkerButton, _state.ActiveTool == ToolKind.Highlighter);
        MarkSelected(LineButton, _state.ActiveTool == ToolKind.Line);
        MarkSelected(ArrowButton, _state.ActiveTool == ToolKind.Arrow);
        MarkSelected(SelectButton, _state.ActiveTool == ToolKind.Select);
        MarkSelected(EraserButton, _state.ActiveTool == ToolKind.EraserStroke);
        MarkSelected(HideButton, _state.InkVisible);
        var c = _state.ActiveColor;
        foreach (var sw in _swatches)
            if (sw.Tag is Rgba rc)
            {
                sw.BorderBrush = rc.Equals(c) ? Brushes.White : Brushes.Transparent;
                sw.BorderThickness = rc.Equals(c) ? new Thickness(2) : new Thickness(0);
            }
        _syncingSlider = true;
        try
        {
            // Slider 1–10; presets maiores (ex. marker 18) pinam no máximo até ajuste.
            ThicknessSlider.Value = Math.Clamp(_state.ActiveWidth, 1f, 10f);
            ThicknessValue.Text = $"{_state.ActiveWidth:F0}";
        }
        finally { _syncingSlider = false; }
        var modeBrush = _state.IsDrawMode ? Brushes.LimeGreen : Brushes.OrangeRed;
        HeaderModeDot.Fill = modeBrush;
        CollapsedModeDot.Fill = modeBrush;
        string modeTip = _state.IsDrawMode ? "Modo desenho (clique e arraste para riscar)"
            : "Modo interagir (cliques atravessam — volte pela toolbar)";
        HeaderModeDot.ToolTip = modeTip;
        CollapsedModeDot.ToolTip = modeTip;
        CollapsedColorDot.Fill = _collapsedColorBrush;
        _collapsedColorBrush.Color = Color.FromRgb(c.R, c.G, c.B);
        CollapsedToolText.Text = ShortToolName.TryGetValue(_state.ActiveTool, out var name) ? name : "?";
        StatusText.Text = $"mons={_overlays.Count} modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"tool={_state.ActiveTool} cor=#{c.R:X2}{c.G:X2}{c.B:X2} w={_state.ActiveWidth:F1} " +
            $"strokes={_state.StrokeCount} pts={_state.PointCount} " +
            $"caps={_state.ScreenCount} sel={_state.SelectedScreenId?.ToString() ?? "-"} " +
            $"p50={_primary.SurfaceControl.ProcessingP50Ms:F2}ms";
    }

    private static void MarkSelected(Button b, bool selected)
    {
        b.Background = selected ? SelBg : Brushes.Transparent;
        b.BorderBrush = selected ? SelBorder : Brushes.Transparent;
    }

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void TryDrag()
    {
        try { DragMove(); }
        catch { /* clique sem arrasto ou headless: ignora */ }
    }

    private void OnToggleCollapse(object sender, RoutedEventArgs e)
    {
        if (_animating) { Log.Info("toggle ignorado: animação em curso"); return; }
        if (_collapsed) ExpandAnimated();
        else CollapseAnimated();
    }

    private void CollapseAnimated()
    {
        _collapsed = true;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapsedBar.Visibility = Visibility.Visible;
        UpdateLayout();
        double targetW = CollapsedBar.DesiredSize.Width + 20;
        double targetH = CollapsedBar.DesiredSize.Height + 16;
        AnimateSize(targetW, targetH);
        Log.Info("toolbar recolhida");
    }

    private void ExpandAnimated()
    {
        _collapsed = false;
        CollapsedBar.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        AnimateSize(_expandedW, _expandedH);
        Log.Info("toolbar expandida");
    }

    // ~130 ms ease-out cúbico em Width+Height (rápido, sem loop contínuo).
    // Wall-clock (Stopwatch): termina em 130 ms reais mesmo com ticks lentos.
    private void AnimateSize(double toW, double toH)
    {
        _animTimer?.Stop();
        double fromW = Width, fromH = Height;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const double durationMs = 130;
        _animating = true;
        _animTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / durationMs);
            double e = 1 - Math.Pow(1 - t, 3);
            Width = fromW + (toW - fromW) * e;
            Height = fromH + (toH - fromH) * e;
            if (t >= 1)
            {
                _animTimer?.Stop();
                _animating = false;
            }
        };
        _animTimer.Start();
    }

    internal bool IsAnimating => _animating;
}
