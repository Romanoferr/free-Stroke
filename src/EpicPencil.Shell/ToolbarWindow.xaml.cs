// Toolbar mínima (semente da toolbar final): todos os comandos do incremento
// atual + atalhos LOCAIS (a overlay nunca tem foco, então as teclas vivem aqui).

using System.Windows;
using System.Windows.Input;
using EpicPencil.Core;

namespace EpicPencil.Shell;

public partial class ToolbarWindow : Window
{
    private readonly AppState _state;
    private readonly OverlayWindow _overlay;

    public ToolbarWindow(AppState state, OverlayWindow overlay)
    {
        InitializeComponent();
        _state = state;
        _overlay = overlay;
        state.StateChanged += RefreshStatus;
        PreviewKeyDown += OnKey;
        RefreshStatus();
    }

    private void OnPen(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.Pen);
    private void OnEraser(object sender, RoutedEventArgs e) => _state.SetTool(ToolKind.EraserStroke);
    private void OnUndo(object sender, RoutedEventArgs e) => _state.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _state.Redo();
    private void OnClear(object sender, RoutedEventArgs e) => _state.Clear();
    private void OnMode(object sender, RoutedEventArgs e) => _state.SetDrawMode(!_state.IsDrawMode);
    private void OnHide(object sender, RoutedEventArgs e) => _state.SetInkVisible(!_state.InkVisible);

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P) _state.SetTool(ToolKind.Pen);
        else if (e.Key == Key.E) _state.SetTool(ToolKind.EraserStroke);
        else if (e.Key == Key.C) _state.Clear();
        else if (e.Key == Key.H) _state.SetInkVisible(!_state.InkVisible);
        else if (e.Key == Key.Escape) _state.SetDrawMode(false); // pânico local: solta o mouse
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _state.Redo();
        else return;
        e.Handled = true;
    }

    private void RefreshStatus()
    {
        ModeButton.Content = _state.IsDrawMode ? "Interagir" : "Desenhar";
        HideButton.Content = _state.InkVisible ? "Esconder (H)" : "Mostrar (H)";
        PenButton.FontWeight = _state.ActiveTool == ToolKind.Pen ? FontWeights.Bold : FontWeights.Normal;
        EraserButton.FontWeight = _state.ActiveTool == ToolKind.EraserStroke ? FontWeights.Bold : FontWeights.Normal;
        StatusText.Text = $"modo={(_state.IsDrawMode ? "desenho" : "interagir")} " +
            $"tool={_state.ActiveTool} strokes={_state.StrokeCount} pts={_state.PointCount} " +
            $"p50={_overlay.SurfaceControl.ProcessingP50Ms:F2}ms undo={_state.CanUndo} redo={_state.CanRedo}";
    }
}
