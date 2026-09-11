using System.Windows;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public partial class App : Application
{
    public static int SelfTestExitCode;

    private AppState? _state;
    private List<OverlayWindow> _overlays = new();
    private ToolbarWindow? _toolbar;
    private System.Windows.Threading.DispatcherTimer? _topologyTimer;
    private bool _rebuilding;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Init();
        Log.Info($"startup args=[{string.Join(" ", e.Args)}]");
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("unhandled exception (app segue rodando)", ex.Exception);
            ex.Handled = true;
        };
        Exit += (_, _) => Log.Info("shutdown");
        if (e.Args.Contains("--selftest"))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SelfTestExitCode = ShellSelfTest.Run();
                Log.Info($"selftest exit={SelfTestExitCode}");
                Shutdown(SelfTestExitCode);
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }
        else
        {
            _state = new AppState();
            RebuildOverlays();
            Log.Info("overlay(s)+toolbar exibidos");
        }
        base.OnStartup(e);
    }

    // Enumera monitores → 1 overlay por work area. Mesmo AppState/documento
    // compartilhado (espaço global); cada superfície converte na borda.
    private void RebuildOverlays()
    {
        if (_state is null) return;
        // Enumera ANTES de fechar: layout vazio aborta sem destruir nada.
        var layout = MonitorLayout.Enumerate();
        if (layout.Count == 0)
        {
            Log.Warn("nenhum monitor enumerado — rebuild adiado");
            return;
        }
        _rebuilding = true;
        try
        {
            // Solta a toolbar antes de fechar os overlays: janela owned fecha
            // junto com o dono (cascata → Closed da toolbar → Shutdown).
            // Owner só pode ser trocado com a toolbar oculta (WPF proíbe visível).
            if (_toolbar is not null)
            {
                _toolbar.Hide();
                _toolbar.Owner = null;
            }
            foreach (var old in _overlays)
            {
                old.TopologyChanged -= ScheduleRebuild;
                old.SuppressCloseShutdown = true;
                old.Close();
            }
            _overlays.Clear();

            Log.Info($"topologia: {layout.Count} monitor(es) " +
                string.Join(" ", layout.Select(m =>
                    $"[#{m.Id} {m.DeviceName} {m.WorkW}x{m.WorkH}@({m.WorkX},{m.WorkY}) prim={m.IsPrimary} dpi={m.DpiScaleX:F2}]")));

            foreach (var monitor in layout)
            {
                var overlay = new OverlayWindow(_state, monitor);
                overlay.TopologyChanged += ScheduleRebuild;
                // REQ1: overlay é área de DESENHO; a toolbar (dono exibido antes
                // de atribuir Owner) fica acima por ownership do OS.
                // (WPF exige o dono exibido antes de atribuir Owner.)
                overlay.Show();
                _overlays.Add(overlay);
            }

            var primary = _overlays.FirstOrDefault(o => o.Monitor.IsPrimary) ?? _overlays[0];
            if (_toolbar is null)
            {
                _toolbar = new ToolbarWindow(_state, _overlays, primary);
            }
            else
            {
                _toolbar.Retarget(_overlays, primary);
            }
            _toolbar.Owner = primary;
            if (!_toolbar.IsVisible) _toolbar.Show();
            var flow = new ScreenCaptureFlow(_toolbar);
            foreach (var overlay in _overlays)
                overlay.SurfaceControl.CaptureFlow = flow;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    // WM_DISPLAYCHANGE chega em cada overlay; debounce evita N rebuilds.
    private void ScheduleRebuild()
    {
        if (_rebuilding) return;
        if (_topologyTimer is null)
        {
            _topologyTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _topologyTimer.Tick += (_, _) =>
            {
                _topologyTimer?.Stop();
                Log.Info("rebuild de topologia (debounce)");
                RebuildOverlays();
            };
        }
        _topologyTimer.Stop();
        _topologyTimer.Start();
    }
}
