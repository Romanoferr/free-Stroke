// Self-test headless do spike (executável no CI/dev sem interação):
//   S1.Overlay.exe --selftest
// Critérios S1/S3: bit TRANSPARENT alterna de verdade, toggle p95 < 50 ms,
// janela nunca vira foreground, render tier reportado.

using System.Diagnostics;
using System.Windows;

namespace S1.Overlay;

internal static class SelfTest
{
    public static int Run()
    {
        var window = new MainWindow();
        window.Show(); // ShowActivated=False no XAML: exibe sem ativar
        var ctl = window.Controller;

        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            System.Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }

        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        Check(hwnd != IntPtr.Zero, "HWND criado");

        long ex0 = Native.GetExStyle(hwnd);
        Check((ex0 & Native.WS_EX_TRANSPARENT) == 0, "inicia em modo desenho (sem TRANSPARENT)");
        Check((ex0 & Native.WS_EX_NOACTIVATE) != 0, "WS_EX_NOACTIVATE aplicado");
        Check((ex0 & Native.WS_EX_TOOLWINDOW) != 0, "WS_EX_TOOLWINDOW aplicado");

        var times = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            bool enable = i % 2 == 0;
            times.Add(ctl.SetClickThrough(enable));
            long ex = Native.GetExStyle(hwnd);
            bool bit = (ex & Native.WS_EX_TRANSPARENT) != 0;
            if (bit != enable)
                Check(false, $"bit TRANSPARENT acompanha toggle (iter {i})");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
        Check(true, "20 toggles sem excecao");
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        System.Console.WriteLine($"toggle max={times[^1]:F2} ms p95={p95:F2} ms");
        Check(p95 < 50, "toggle p95 < 50 ms");

        Check(ctl.NeverTookFocus(), "janela nunca virou foreground");
        System.Console.WriteLine($"RenderCapability.Tier={System.Windows.Media.RenderCapability.Tier >> 16}");
        System.Console.WriteLine($"Topmost={window.Topmost}");

        ctl.SetClickThrough(false);
        window.Close();
        System.Console.WriteLine(failures.Count == 0 ? "SELFTEST OK" : $"SELFTEST FALHOU: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }
}
