using System.Windows;
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
    }

    public InkSurface SurfaceControl => Surface;

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
    }

    private void ApplyState()
    {
        if (_hwnd == IntPtr.Zero) return;
        OverlayBehavior.SetClickThrough(_hwnd, !_state.IsDrawMode);
        Surface.Visibility = _state.InkVisible ? Visibility.Visible : Visibility.Hidden;
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
