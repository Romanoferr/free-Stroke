// Controlador do overlay (lógica promovível a EpicPencil.Windows).
// Pontos não óbvios:
//  1. WS_EX_TRANSPARENT sozinho NÃO basta: sem WS_EX_NOACTIVATE + MA_NOACTIVATE
//     a janela ainda pode ativar ao clicar (rouba foco do app abaixo).
//  2. Após SetWindowLongPtr é obrigatório SetWindowPos(FRAMECHANGED),
//     senão o estilo novo não vale até o próximo resize.
//  3. Reassert de Topmost é sob demanda (evento), nunca polling — protege idle ~0%.

using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace S1.Overlay;

internal sealed class OverlayController
{
    private readonly Window _window;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _clickThrough;

    public OverlayController(Window window) => _window = window;
    public bool IsClickThrough => _clickThrough;

    public void Attach()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndHook);

        long ex = Native.GetExStyle(_hwnd);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex &= ~Native.WS_EX_TRANSPARENT; // começa em modo desenho (clicável)
        Native.SetExStyle(_hwnd, ex);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    public void Detach() => _source?.RemoveHook(WndHook);

    // Retorna o tempo do toggle (aceite S3: p95 < 50 ms).
    public double SetClickThrough(bool enable)
    {
        var sw = Stopwatch.StartNew();
        long ex = Native.GetExStyle(_hwnd);
        ex = enable ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetExStyle(_hwnd, ex);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        sw.Stop();
        _clickThrough = enable;
        return sw.Elapsed.TotalMilliseconds;
    }

    public void ReassertTopmost() =>
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOREDRAW);

    public bool NeverTookFocus() => Native.GetForegroundWindow() != _hwnd;

    private static IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true; // desenha sem ativar: o foco fica no app abaixo
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }
}
