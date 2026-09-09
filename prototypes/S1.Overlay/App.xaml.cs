using System.Windows;

namespace S1.Overlay;

public partial class App : Application
{
    public static int SelfTestExitCode = 0;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            // Roda o self-test no dispatcher e encerra com o código resultante.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SelfTestExitCode = SelfTest.Run();
                Shutdown(SelfTestExitCode);
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }
        else
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        base.OnStartup(e);
    }
}
