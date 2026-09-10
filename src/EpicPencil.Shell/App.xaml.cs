using System.Windows;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

public partial class App : Application
{
    public static int SelfTestExitCode;

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
            var state = new AppState();
            var overlay = new OverlayWindow(state);
            var toolbar = new ToolbarWindow(state, overlay);
            // REQ1: toolbar é área de INTERAÇÃO (janela real, acima do overlay por
            // ownership do OS) — cliques nela nunca alcançam a camada de desenho.
            // Arrastável pela titlebar nativa para qualquer posição da tela.
            // (WPF exige o dono exibido antes de atribuir Owner.)
            overlay.Show();
            toolbar.Owner = overlay;
            toolbar.Show();
            Log.Info("overlay+toolbar exibidos");
        }
        base.OnStartup(e);
    }
}
