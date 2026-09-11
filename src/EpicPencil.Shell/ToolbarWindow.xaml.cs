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

    // Self-test fecha janelas sem encerrar o processo (produção: fechar = sair).
    public bool SuppressCloseShutdown { get; set; }

    public ToolbarWindow(AppState state, IReadOnlyList<OverlayWindow> overlays, OverlayWindow primary)
    {
        InitializeComponent();
        _state = state;
        _overlays = overlays;
        _primary = primary;
        foreach (var color in Palette)
        {
            var swatch = new Button
            {
                Width = 28,
                Height = 24,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                BorderBrush = Brushes.Gray,
                ToolTip = $"Cor #{color.R:X2}{color.G:X2}{color.B:X2}",
                Tag = color,
            };
            swatch.Click += (_, _) => { _state.ActiveColor = color; RefreshStatus(); };
            ColorRow.Children.Add(swatch);
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
    private void OnWidthS(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(0);
    private void OnWidthM(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(1);
    private void OnWidthL(object sender, RoutedEventArgs e) => _state.SetActiveWidthPreset(2);
    private void OnUndo(object sender, RoutedEventArgs e) => _state.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _state.Redo();
    private void OnClear(object sender, RoutedEventArgs e) => _state.Clear();
    private void OnMode(object sender, RoutedEventArgs e) => _state.SetDrawMode(!_state.IsDrawMode);
    private void OnHide(object sender, RoutedEventArgs e) => _state.SetInkVisible(!_state.InkVisible);
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
        else if (e.Key == Key.F9) _state.SetInkVisible(!_state.InkVisible); // local: sem conflito global
        else if (e.Key == Key.Escape) _state.SetDrawMode(false); // pânico local: solta o mouse
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Redo();
        else return;
        e.Handled = true;
    }

    private void RefreshStatus()
    {
        ModeButton.Content = _state.IsDrawMode ? "Interagir" : "Desenhar";
        HideButton.Content = _state.InkVisible ? "Esconder" : "Mostrar";
        PenButton.FontWeight = _state.ActiveTool == ToolKind.Pen ? FontWeights.Bold : FontWeights.Normal;
        PencilButton.FontWeight = _state.ActiveTool == ToolKind.Pencil ? FontWeights.Bold : FontWeights.Normal;
        MarkerButton.FontWeight = _state.ActiveTool == ToolKind.Highlighter ? FontWeights.Bold : FontWeights.Normal;
        LineButton.FontWeight = _state.ActiveTool == ToolKind.Line ? FontWeights.Bold : FontWeights.Normal;
        ArrowButton.FontWeight = _state.ActiveTool == ToolKind.Arrow ? FontWeights.Bold : FontWeights.Normal;
        SelectButton.FontWeight = _state.ActiveTool == ToolKind.Select ? FontWeights.Bold : FontWeights.Normal;
        EraserButton.FontWeight = _state.ActiveTool == ToolKind.EraserStroke ? FontWeights.Bold : FontWeights.Normal;
        var c = _state.ActiveColor;
        StatusText.Text = $"mons={_overlays.Count} modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"tool={_state.ActiveTool} cor=#{c.R:X2}{c.G:X2}{c.B:X2} w={_state.ActiveWidth:F1} " +
            $"strokes={_state.StrokeCount} pts={_state.PointCount} " +
            $"caps={_state.ScreenCount} sel={_state.SelectedScreenId?.ToString() ?? "-"} " +
            $"p50={_primary.SurfaceControl.ProcessingP50Ms:F2}ms";
    }
}
