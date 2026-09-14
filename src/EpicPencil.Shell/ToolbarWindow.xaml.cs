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
    // Nome humano no tooltip (hex é para devs).
    private static readonly (Rgba Color, string Name)[] Palette =
    {
        (new(0, 0, 0), "Preto"),
        (new(255, 255, 255), "Branco"),
        (new(255, 0, 0), "Vermelho"),
        (new(0, 120, 215), "Azul"),
        (new(0, 180, 0), "Verde"),
        (new(255, 235, 59), "Amarelo"),
    };

    private readonly AppState _state;
    private IReadOnlyList<OverlayWindow> _overlays;
    private OverlayWindow _primary;
    private readonly List<Button> _swatches = new();

    // Selected visual (aplicado em código; hover/pressed vivem no XAML).
    // Cores vêm do Theme.xaml (mesclado nos recursos da janela) e são congeladas
    // p/ render barato. Resolvidas por instância (self-test não carrega App.xaml).
    private readonly SolidColorBrush _selBg;
    private readonly SolidColorBrush _selBorder;
    private readonly SolidColorBrush _modeDrawBrush;
    private readonly SolidColorBrush _modeInteractBrush;
    private readonly SolidColorBrush _textSecondaryFrozen;
    private readonly SolidColorBrush _collapsedColorBrush = new(Color.FromRgb(255, 0, 0));

    private SolidColorBrush FrozenFromTheme(string key)
    {
        if (TryFindResource(key) is not SolidColorBrush src) throw new InvalidOperationException($"Theme token ausente: {key}");
        return (SolidColorBrush)src.GetAsFrozen();
    }

    private static readonly Dictionary<ToolKind, string> ShortToolName = new()
    {
        [ToolKind.Pen] = "Caneta",
        [ToolKind.Pencil] = "Lápis",
        [ToolKind.Highlighter] = "Marca",
        [ToolKind.Line] = "Linha",
        [ToolKind.Arrow] = "Seta",
        [ToolKind.Rectangle] = "Retângulo",
        [ToolKind.Circle] = "Círculo",
        [ToolKind.Select] = "Seleção",
        [ToolKind.EraserStroke] = "Borracha",
        [ToolKind.Text] = "Texto",
    };

    // Animação de collapse (130 ms, ease-out). Só apresentação; headless nunca alterna.
    private bool _collapsed;
    private bool _animating;
    private bool _syncingSlider; // RefreshStatus → slider sem reentrância
    private bool _syncingFont; // RefreshStatus → combo sem reentrância
    private double _expandedW = 380;
    private double _expandedH = 320;
    private System.Windows.Threading.DispatcherTimer? _animTimer;

    // Self-test fecha janelas sem encerrar o processo (produção: fechar = sair).
    public bool SuppressCloseShutdown { get; set; }
    public bool IsCollapsed => _collapsed;

    // Flow de captura p/ Ctrl+C/Ctrl+S sem seleção (BitBlt one-shot sob demanda).
    // O App injeta o ScreenCaptureFlow compartilhado; fallback cria um próprio
    // (mesma classe, sem segundo sistema) p/ construção direta (ex. self-test).
    public IScreenCaptureFlow? ExportFlow { get; set; }

    public ToolbarWindow(AppState state, IReadOnlyList<OverlayWindow> overlays, OverlayWindow primary)
    {
        InitializeComponent();
        _selBg = FrozenFromTheme("AccentBgBrush");
        _selBorder = FrozenFromTheme("AccentBrush");
        _modeDrawBrush = FrozenFromTheme("ModeDrawBrush");
        _modeInteractBrush = FrozenFromTheme("ModeInteractBrush");
        _textSecondaryFrozen = FrozenFromTheme("TextSecondaryBrush");
        _state = state;
        _overlays = overlays;
        _primary = primary;
        // Assinado aqui (não no XAML): o Value inicial dispara ValueChanged
        // durante InitializeComponent, antes de _state existir (NRE → hang).
        ThicknessSlider.ValueChanged += OnThicknessChanged;
        foreach (int size in new[] { 12, 16, 20, 24, 32, 40, 48, 64 })
            FontSizeBox.Items.Add(size.ToString());
        FontSizeBox.SelectedItem = ((int)_state.ActiveFontSizeDip).ToString();
        FontSizeBox.SelectionChanged += OnFontSizeChanged;
        foreach (var (color, name) in Palette)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                ToolTip = name,
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
    private void OnToolRectangle(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Rectangle);
    private void OnToolCircle(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Circle);
    private void OnToolSelect(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Select);
    private void OnToolText(object sender, RoutedEventArgs e) => ActivateTextTool();
    // Texto exige input do overlay: ao ativá-la garante modo desenho visível
    // (sem isso, no modo interagir o clique atravessaria e nada abriria).
    private void ActivateTextTool()
    {
        _state.SetTool(ToolKind.Text);
        _state.SetInkVisible(true);
        _state.SetDrawMode(true);
    }
    private void OnDeleteScreen(object sender, RoutedEventArgs e) => _state.DeleteSelectedObject();
    private void OnToolEraser(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.EraserStroke);
    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSlider) return;
        _state.ActiveWidth = (float)Math.Round(e.NewValue);
        RefreshStatus(); // ActiveWidth não dispara StateChanged (só presets)
    }
    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFont) return;
        if (FontSizeBox.SelectedItem is string s && float.TryParse(s, out float size))
        {
            // Sempre define o padrão p/ novos textos; com texto selecionado,
            // aplica nele também (um comando, um Ctrl+Z desfaz).
            _state.SetActiveFontSize(size);
            if (_state.SelectedTextId is int tid)
                ApplySelectedTextSize(tid, size);
            RefreshStatus();
        }
    }

    private void ApplySelectedTextSize(int id, float sizeDip)
    {
        var t = _state.Texts.FirstOrDefault(x => x.Id == id);
        if (t is null) return;
        float sizePx = sizeDip * _primary.SurfaceControl.Frame.PxPerDipX;
        if (Math.Abs(t.FontSizePx - sizePx) < 0.01) return;
        var (w, h) = TextMeasure.Measure(t.Content, t.FontFamily, sizePx);
        _state.UpdateText(id, t.Content, sizePx, w, h);
    }
    private void OnUndo(object sender, RoutedEventArgs e) => _state.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _state.Redo();
    private void OnWidthPreset0(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(0);
    private void OnWidthPreset1(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(1);
    private void OnWidthPreset2(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(2);
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

    // Despacho único de hotkeys LOCAIS via HotkeyRouter (REQ3): Ctrl+C/Ctrl+S
    // nunca ativam C/S, teclas com modificador nunca trocam ferramenta, e cada
    // gesto gera exatamente UMA ação (e.Handled = true).
    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (HotkeyRouter.Resolve(e.Key, Keyboard.Modifiers))
        {
            case HotkeyAction.ToolPen: _state.SetTool(ToolKind.Pen); break;
            case HotkeyAction.ToolPencil: _state.SetTool(ToolKind.Pencil); break;
            case HotkeyAction.ToolHighlighter: _state.SetTool(ToolKind.Highlighter); break;
            case HotkeyAction.ToolLine: _state.SetTool(ToolKind.Line); break;
            case HotkeyAction.ToolArrow: _state.SetTool(ToolKind.Arrow); break;
            case HotkeyAction.ToolRectangle: _state.SetTool(ToolKind.Rectangle); break;
            case HotkeyAction.ToolCircle: _state.SetTool(ToolKind.Circle); break;
            case HotkeyAction.ToolSelect: _state.SetTool(ToolKind.Select); break;
            case HotkeyAction.ToolText: ActivateTextTool(); break;
            case HotkeyAction.ToolEraser: _state.SetTool(ToolKind.EraserStroke); break;
            case HotkeyAction.WidthPreset0: _state.SetActiveWidthPreset(0); break;
            case HotkeyAction.WidthPreset1: _state.SetActiveWidthPreset(1); break;
            case HotkeyAction.WidthPreset2: _state.SetActiveWidthPreset(2); break;
            case HotkeyAction.Clear: _state.Clear(); break;
            case HotkeyAction.DeleteScreen: _state.DeleteSelectedObject(); break;
            case HotkeyAction.ToggleInkMode: ToggleInkMode(); break;
            case HotkeyAction.HideInk: // Escape: com seleção, só desseleciona (REQ2); sem seleção, oculta
                if (_state.HasSelection) _state.ClearSelection();
                else HideInk();
                break;
            case HotkeyAction.Undo: _state.Undo(); break;
            case HotkeyAction.Redo: _state.Redo(); break;
            case HotkeyAction.CopyCapture: _ = CopyCaptureAsync(); break;
            case HotkeyAction.SaveCapture: _ = SaveCaptureAsync(); break;
            case HotkeyAction.None:
            default: return;
        }
        e.Handled = true;
    }

    // Bitmap de exportação: captura fresca da região + conteúdo do modelo.
    // Região = bounds atuais da seleção (ou tela virtual sem seleção). Os
    // overlays são ocultados durante o BitBlt (nenhum chrome nosso entra nos
    // pixels, em qualquer driver/GPU); capturas, tinta e textos vêm do modelo.
    // Não move, apaga, altera a seleção nem troca a ferramenta. Null se falhar.
    private async Task<System.Windows.Media.Imaging.BitmapSource?> RenderExportBitmapAsync()
    {
        var sel = CaptureExport.TryGetSelectedScreen(_state);
        RectD region = sel?.Bounds
            ?? CaptureExport.GetFullVirtualRect(MonitorLayout.Enumerate());
        var flow = ExportFlow ??= new ScreenCaptureFlow(this);
        SetOverlaysVisible(false);
        CapturedImage? img;
        try { img = await flow.CaptureRegionGlobalPxAsync(region); }
        finally { SetOverlaysVisible(true); }
        if (img is null) return null;
        var (l, t, _, _) = CaptureExport.SnapToPixels(region);
        var captured = CaptureExport.ToBitmapSource(img.Bgra, img.PixelWidth, img.PixelHeight);
        return CaptureExport.CompositeModel(captured, l, t,
            _state.Strokes, _state.Screens, _state.Texts,
            _state.Rectangles, _state.Circles);
    }

    // Oculta/mostra os overlays p/ o BitBlt não carregar chrome (determinístico;
    // o flow já esconde a toolbar). Hide/Show síncronos + 80 ms do flow p/ o DWM
    // recompor sem nossas janelas. Restaura sempre (finally no chamador).
    internal void SetOverlaysVisible(bool visible)
    {
        foreach (var o in _overlays)
        {
            if (visible) o.Show();
            else o.Hide();
        }
    }

    // Ctrl+C: vai ao clipboard do Windows (Ctrl+V no Paint/Word/Discord).
    private async Task CopyCaptureAsync()
    {
        try
        {
            var bmp = await RenderExportBitmapAsync();
            if (bmp is null)
            {
                Log.Warn("Ctrl+C cancelado: captura da tela falhou");
                return;
            }
            CaptureExport.CopyToClipboard(bmp);
        }
        catch (Exception ex) { Log.Error("falha no Ctrl+C", ex); }
    }

    // Ctrl+S: PNG via diálogo padrão do Windows.
    private async Task SaveCaptureAsync()
    {
        try
        {
            var sel = CaptureExport.TryGetSelectedScreen(_state);
            var bmp = await RenderExportBitmapAsync();
            if (bmp is null)
            {
                Log.Warn("Ctrl+S cancelado: captura da tela falhou");
                return;
            }
            CaptureExport.SaveWithDialog(this, bmp,
                sel is not null ? "captura-selecao.png" : "captura.png");
        }
        catch (Exception ex) { Log.Error("falha no Ctrl+S", ex); }
    }

    private void RefreshStatus()
    {
        MarkSelected(PenButton, _state.ActiveTool == ToolKind.Pen);
        MarkSelected(PencilButton, _state.ActiveTool == ToolKind.Pencil);
        MarkSelected(MarkerButton, _state.ActiveTool == ToolKind.Highlighter);
        MarkSelected(LineButton, _state.ActiveTool == ToolKind.Line);
        MarkSelected(ArrowButton, _state.ActiveTool == ToolKind.Arrow);
        MarkSelected(RectangleButton, _state.ActiveTool == ToolKind.Rectangle);
        MarkSelected(CircleButton, _state.ActiveTool == ToolKind.Circle);
        MarkSelected(SelectButton, _state.ActiveTool == ToolKind.Select);
        MarkSelected(EraserButton, _state.ActiveTool == ToolKind.EraserStroke);
        MarkSelected(TextButton, _state.ActiveTool == ToolKind.Text);
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
            // Slider 1–64 (range real do modelo; presets maiores, ex. marker 18, cabem).
            ThicknessSlider.Value = Math.Clamp(_state.ActiveWidth, 1f, 64f);
            ThicknessValue.Text = $"{_state.ActiveWidth:F0}";
        }
        finally { _syncingSlider = false; }
        // Preset S/M/G ativo: realce quando a espessura atual == preset canônico.
        MarkPreset(WidthPreset0Button, StrokeSpec.WidthPreset(_state.ActiveTool, 0));
        MarkPreset(WidthPreset1Button, StrokeSpec.WidthPreset(_state.ActiveTool, 1));
        MarkPreset(WidthPreset2Button, StrokeSpec.WidthPreset(_state.ActiveTool, 2));
        // Estados disabled: indisponível não pode parecer clicável (nem sumir).
        UndoButton.IsEnabled = _state.CanUndo;
        RedoButton.IsEnabled = _state.CanRedo;
        DeleteObjectButton.IsEnabled = _state.HasSelection;
        _syncingFont = true;
        try
        {
            // Combo mostra o tamanho do texto selecionado (p/ aplicar nele) ou
            // o padrão p/ novos textos. Só valores da lista (sempre válidos).
            string want = ((int)_state.ActiveFontSizeDip).ToString();
            if (_state.SelectedTextId is int tid
                && _state.Texts.FirstOrDefault(t => t.Id == tid) is { } tt)
            {
                float dip = tt.FontSizePx / _primary.SurfaceControl.Frame.PxPerDipX;
                string have = ((int)Math.Round(dip)).ToString();
                if (FontSizeBox.Items.Contains(have)) want = have;
            }
            if (!Equals(FontSizeBox.SelectedItem, want)) FontSizeBox.SelectedItem = want;
        }
        finally { _syncingFont = false; }
        // Contextual: controle de fonte só quando faz sentido (ferramenta Texto
        // ou texto selecionado p/ redimensionar). Não ocupa espaço permanente.
        bool fontRelevant = _state.ActiveTool == ToolKind.Text || _state.SelectedTextId is not null;
        FontSlot.Visibility = fontRelevant ? Visibility.Visible : Visibility.Collapsed;
        // Modo: cor + forma no olho (toggle) e no dot da barra recolhida —
        // nunca só cor (acessibilidade). Olho aberto = tinta visível/desenho.
        var modeBrush = _state.IsDrawMode ? _modeDrawBrush : _modeInteractBrush;
        CollapsedModeDot.Fill = modeBrush;
        string modeTip = _state.IsDrawMode ? "Modo desenho (clique e arraste para riscar)"
            : "Modo interagir (cliques atravessam — volte pela toolbar)";
        CollapsedModeDot.ToolTip = modeTip;
        bool ink = _state.InkVisible;
        EyeOpenPath.Visibility = ink ? Visibility.Visible : Visibility.Collapsed;
        EyePupil.Visibility = ink ? Visibility.Visible : Visibility.Collapsed;
        EyeClosedPath.Visibility = ink ? Visibility.Collapsed : Visibility.Visible;
        HideButton.Background = ink ? Brushes.Transparent : _selBg;
        HideButton.BorderBrush = ink ? Brushes.Transparent : _selBorder;
        HideButton.ToolTip = ink
            ? "Ocultar desenho e interagir com o que está abaixo (F9)"
            : "Mostrar desenho (F9)";
        CollapsedColorDot.Fill = _collapsedColorBrush;
        _collapsedColorBrush.Color = Color.FromRgb(c.R, c.G, c.B);
        CollapsedToolText.Text = ShortToolName.TryGetValue(_state.ActiveTool, out var name) ? name : "?";
        StatusText.Text = $"mons={_overlays.Count} modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"tool={_state.ActiveTool} cor=#{c.R:X2}{c.G:X2}{c.B:X2} w={_state.ActiveWidth:F1} " +
            $"strokes={_state.StrokeCount} pts={_state.PointCount} " +
            $"caps={_state.ScreenCount} sel={_state.SelectedScreenId?.ToString() ?? "-"} " +
            $"txt={_state.TextCount} ret={_state.RectangleCount} circ={_state.CircleCount} " +
            $"f={_state.ActiveFontSizeDip:F0} " +
            $"p50={_primary.SurfaceControl.ProcessingP50Ms:F2}ms";
    }

    private void MarkSelected(Button b, bool selected)
    {
        b.Background = selected ? _selBg : Brushes.Transparent;
        b.BorderBrush = selected ? _selBorder : Brushes.Transparent;
    }

    private void MarkPreset(Button b, float canonicalWidth)
    {
        bool active = Math.Abs(_state.ActiveWidth - canonicalWidth) < 0.01f;
        b.Background = active ? _selBg : Brushes.Transparent;
        b.BorderBrush = active ? _selBorder : Brushes.Transparent;
        b.Foreground = active ? _selBorder : _textSecondaryFrozen;
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
