using System.Windows;
using System.Windows.Media;

namespace S1.Overlay;

public partial class MainWindow : Window
{
    private readonly OverlayController _controller;

    public MainWindow()
    {
        InitializeComponent();
        _controller = new OverlayController(this);
        SourceInitialized += (_, _) => _controller.Attach();
        Closed += (_, _) => _controller.Detach();
        Loaded += (_, _) => TierText.Text = $"Render tier: {RenderCapability.Tier >> 16} (N-03: tier 0 = software fallback)";
        // Fullscreen manual: WPF proíbe ShowActivated=False + WindowState=Maximized.
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    internal OverlayController Controller => _controller;

    public void Toggle()
    {
        bool enable = !_controller.IsClickThrough;
        double ms = _controller.SetClickThrough(enable);
        StatusText.Text = enable ? "Modo: INTERAGIR (click-through)" : "Modo: DESENHO (clicavel)";
        LogText.Text += $"toggle->{(enable ? "through" : "draw")} {ms:F2} ms | foco preservado: {_controller.NeverTookFocus()}\n";
    }

    private void OnToggle(object sender, RoutedEventArgs e) => Toggle();
    private void OnTopmost(object sender, RoutedEventArgs e)
    {
        _controller.ReassertTopmost();
        LogText.Text += "reassert topmost (NOACTIVATE)\n";
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
