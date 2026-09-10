using System.Windows;

namespace EpicPencil.Shell;

public partial class App : Application
{
    public static int SelfTestExitCode;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SelfTestExitCode = ShellSelfTest.Run();
                Shutdown(SelfTestExitCode);
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }
        else
        {
            var state = new AppState();
            var overlay = new OverlayWindow(state);
            var toolbar = new ToolbarWindow(state, overlay);
            overlay.Show();
            toolbar.Show();
        }
        base.OnStartup(e);
    }
}
