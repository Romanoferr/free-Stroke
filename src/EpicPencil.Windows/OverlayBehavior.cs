// Lógica do overlay sobre HWND puro (testável sem WPF: só IntPtr).
// A Shell conecta TryHandleMouseActivate ao hook HwndSource e chama os demais
// métodos com o handle da OverlayWindow.

namespace EpicPencil.Windows;

public static class OverlayBehavior
{
    public static void ApplyBaseStyles(IntPtr hwnd)
    {
        long ex = Native.GetExStyle(hwnd);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex &= ~Native.WS_EX_TRANSPARENT; // começa clicável (modo desenho)
        Native.SetExStyle(hwnd, ex);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    // WS_EX_TRANSPARENT só vale após SetWindowPos(FRAMECHANGED) — sem isso o
    // toggle "funciona" no bit mas o hit-test continua igual (armadilha clássica).
    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        long ex = Native.GetExStyle(hwnd);
        ex = enable ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetExStyle(hwnd, ex);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    public static bool IsClickThrough(IntPtr hwnd) =>
        (Native.GetExStyle(hwnd) & Native.WS_EX_TRANSPARENT) != 0;

    // Reassert sob demanda (evento), nunca polling — protege o idle ~0%.
    public static void ReassertTopmost(IntPtr hwnd) =>
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOREDRAW);

    public static bool IsForeground(IntPtr hwnd) => Native.GetForegroundWindow() == hwnd;

    // Retorna true quando a mensagem foi consumida (a Shell marca handled=true).
    public static bool TryHandleMouseActivate(int msg, out IntPtr result)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            result = new IntPtr(Native.MA_NOACTIVATE); // clica sem ativar
            return true;
        }
        result = IntPtr.Zero;
        return false;
    }
}
