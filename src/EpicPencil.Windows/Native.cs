// Interop Win32 do overlay. Promovido do spike S1 (comportamento idêntico,
// validado pelo self-test: toggle p95 < 8 ms, janela nunca ativa).
// REGRA: nenhum tipo WPF aqui (só IntPtr). O hook HwndSource é ligado na Shell.

using System.Runtime.InteropServices;
using System.Text;

namespace EpicPencil.Windows;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOREDRAW = 0x0008;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public const int SM_CMONITORS = 80;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // Diagnóstico "quem recebe o clique": identifica o HWND sob o ponto.
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    // Ground truth da z-order (quando WindowFromPoint contradiz o visível).
    public const int GWL_STYLE = -16;
    public const long WS_DISABLED = 0x08000000L;
    public const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowStyle(IntPtr hWnd, int nIndex);

    // Sonda WM_NCHITTEST direta: pergunta ao próprio HWND o que ele responde.
    public const int WM_NCHITTEST = 0x84;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Captura de região (BitBlt one-shot). Sem CAPTUREBLT de propósito: janelas
    // layered (nosso overlay/tinta, tooltips) ficam FORA da captura — queremos
    // o conteúdo abaixo, limpo. Coords virtuais (negativas) funcionam.
    public const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight; // negativo = top-down (nossa ordem de bytes)
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hDC, IntPtr hBitmap, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint uUsage);

    public static long GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
            : GetWindowLong32(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hwnd, long style) =>
        _ = IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style))
            : SetWindowLong32(hwnd, GWL_EXSTYLE, unchecked((int)style));
}
