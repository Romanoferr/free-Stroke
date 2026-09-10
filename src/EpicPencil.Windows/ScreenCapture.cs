// Captura one-shot de região via GDI (BitBlt SRCCOPY, sem CAPTUREBLT).
// Decisão (MVP): uma única chamada síncrona por seleção — sem screenshots por
// frame, sem device D3D, sem dependências. Alternativas descartadas: WinRT
// Graphics Capture (async + D3D, overkill p/ one-shot), Desktop Duplication
// (streaming, não one-shot), PrintWindow (por janela, não região).
// Limitações honestas: fullscreen-exclusivo/DRM sai preto; janelas layered
// (tooltips, menus flutuantes) não aparecem; tudo documentado no resumo.

namespace EpicPencil.Windows;

public sealed record CapturedImage(int PixelWidth, int PixelHeight, byte[] Bgra);

public static class ScreenCapture
{
    public const int MaxSidePx = 4096;
    public const int MaxAreaPx = 16 * 1024 * 1024;

    public static CapturedImage? CaptureRegionPx(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxSidePx || height > MaxSidePx)
        {
            Log.Warn($"captura rejeitada: rect inválido {width}x{height}");
            return null;
        }
        if ((long)width * height > MaxAreaPx)
        {
            Log.Warn($"captura rejeitada: área acima do teto ({width}x{height})");
            return null;
        }

        IntPtr hdcScreen = IntPtr.Zero, hdcMem = IntPtr.Zero, hbm = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hdcScreen = Native.GetDC(IntPtr.Zero); // tela virtual (coords negativas OK)
            if (hdcScreen == IntPtr.Zero) return null;
            hdcMem = Native.CreateCompatibleDC(hdcScreen);
            hbm = Native.CreateCompatibleBitmap(hdcScreen, width, height);
            if (hdcMem == IntPtr.Zero || hbm == IntPtr.Zero) return null;
            old = Native.SelectObject(hdcMem, hbm);
            if (!Native.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, left, top, Native.SRCCOPY))
            {
                Log.Warn($"BitBlt falhou em ({left},{top}) {width}x{height}");
                return null;
            }
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down: linha 0 = topo (ordem do WPF)
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            var bytes = new byte[4 * width * height];
            int lines = Native.GetDIBits(hdcMem, hbm, 0, (uint)height, bytes, ref header, 0);
            if (lines != height)
            {
                Log.Warn($"GetDIBits retornou {lines}/{height} linhas");
                return null;
            }
            Log.Info($"captura {width}x{height}px em ({left},{top})");
            return new CapturedImage(width, height, bytes);
        }
        catch (Exception ex)
        {
            Log.Error("falha na captura de região", ex);
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero && hdcMem != IntPtr.Zero) Native.SelectObject(hdcMem, old);
            if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
            if (hdcMem != IntPtr.Zero) Native.DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
