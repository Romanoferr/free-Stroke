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

    // Posicionamento físico exato (px da tela virtual, negativos OK): imune a
    // DPI misto, ao contrário de Left/Top do WPF. Usado no Loaded de cada overlay.
    public static void PlaceAt(IntPtr hwnd, int x, int y, int w, int h) =>
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);

    // WM_DISPLAYCHANGE (plug/unplug/resolução/escala/primário): a Shell observa
    // via hook e reconstrói os overlays com debounce.
    public static bool IsDisplayChange(int msg) => msg == Native.WM_DISPLAYCHANGE;

    public static bool IsForeground(IntPtr hwnd) => Native.GetForegroundWindow() == hwnd;

    public static int MonitorCount()
    {
        try { return Math.Max(1, Native.GetSystemMetrics(Native.SM_CMONITORS)); }
        catch { return -1; }
    }

    // Readback para o log: prova o estado real dos bits (não o pretendido).
    public static string Describe(IntPtr hwnd)
    {
        long ex = Native.GetExStyle(hwnd);
        bool t = (ex & Native.WS_EX_TRANSPARENT) != 0;
        bool na = (ex & Native.WS_EX_NOACTIVATE) != 0;
        bool tw = (ex & Native.WS_EX_TOOLWINDOW) != 0;
        return $"ex=0x{ex:X} transparent={t} noactivate={na} toolwindow={tw}";
    }

    // Diagnóstico "quem recebe o clique": identifica o HWND sob o cursor,
    // distinguindo OVERLAY / TOOLBAR / OUTRO. Amostrar com o cursor parado
    // SOBRE O CANVAS (com atraso) — nunca sobre o botão do teste (mede a toolbar).
    public static bool GetCursor(out int x, out int y)
    {
        if (Native.GetCursorPos(out var pt)) { x = pt.X; y = pt.Y; return true; }
        x = y = 0;
        return false;
    }

    public static string DescribePointOwner(int x, int y, IntPtr overlayHwnd, IntPtr toolbarHwnd)
    {
        try
        {
            IntPtr hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
            if (hit == IntPtr.Zero) return "ponto sem janela (fora de qualquer HWND)";
            var title = new System.Text.StringBuilder(256);
            var cls = new System.Text.StringBuilder(256);
            Native.GetWindowTextW(hit, title, title.Capacity);
            Native.GetClassNameW(hit, cls, cls.Capacity);
            Native.GetWindowThreadProcessId(hit, out uint pid);
            string proc;
            try { proc = System.Diagnostics.Process.GetProcessById(unchecked((int)pid)).ProcessName; }
            catch { proc = "?"; }
            string owner = hit == overlayHwnd ? "OVERLAY (nosso canvas)"
                : hit == toolbarHwnd ? "TOOLBAR (nossa)"
                : $"OUTRO proc={proc}";
            return $"hit=0x{hit.ToInt64():X} dono={owner} class={cls} titulo={title} proc={proc}";
        }
        catch (Exception ex) { return $"falha no diagnóstico: {ex.Message}"; }
    }

    // Sonda NCHITTEST direta no próprio HWND (coords físicas de tela).
    // HTCLIENT=1 (recebe o clique) / HTNOWHERE=0 / HTTRANSPARENT=-1 (atravessa).
    public static string ProbeHitTest(IntPtr hwnd, int x, int y)
    {
        try
        {
            IntPtr lParam = new((y << 16) | (x & 0xFFFF));
            long result = Native.SendMessageW(hwnd, Native.WM_NCHITTEST, IntPtr.Zero, lParam).ToInt64();
            string name = result == 1 ? "HTCLIENT" : result == -1 ? "HTTRANSPARENT" : result == 0 ? "HTNOWHERE" : $"HT({result})";
            return $"nchittest({x},{y})={name}";
        }
        catch (Exception ex) { return $"sonda falhou: {ex.Message}"; }
    }

    // Ground truth da z-order: caminha do topo listando as janelas. Responde
    // "estamos acima ou abaixo do app X?" sem teoria — com a ordem real do OS.
    public static List<string> DescribeZOrder(IntPtr overlayHwnd, IntPtr toolbarHwnd, int max = 20)
    {
        var lines = new List<string>();
        try
        {
            IntPtr hwnd = Native.GetTopWindow(IntPtr.Zero);
            int i = 0;
            while (hwnd != IntPtr.Zero && i < max)
            {
                bool visible = Native.IsWindowVisible(hwnd);
                int style = Native.GetWindowStyle(hwnd, Native.GWL_STYLE);
                bool disabled = (style & (int)Native.WS_DISABLED) != 0;
                string tag = hwnd == overlayHwnd ? ">>OVERLAY<<"
                    : hwnd == toolbarHwnd ? ">TOOLBAR<"
                    : "";
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                string proc;
                try { proc = System.Diagnostics.Process.GetProcessById(unchecked((int)pid)).ProcessName; }
                catch { proc = "?"; }
                var title = new System.Text.StringBuilder(128);
                Native.GetWindowTextW(hwnd, title, title.Capacity);
                string rect = Native.GetWindowRect(hwnd, out var r) ? r.ToString() : "?";
                lines.Add($"z[{i}] 0x{hwnd.ToInt64():X} vis={visible} dis={disabled} rect={rect} {tag} proc={proc} titulo={title}");
                hwnd = Native.GetWindow(hwnd, Native.GW_HWNDNEXT);
                i++;
            }
        }
        catch (Exception ex) { lines.Add($"falha na caminhada: {ex.Message}"); }
        return lines;
    }

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
