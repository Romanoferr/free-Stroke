// Orquestra a captura escondendo a toolbar (janela normal APARECERIA no BitBlt;
// o overlay layered já é excluído sem CAPTUREBLT). 80 ms ≈ 5 frames p/ compor
// sem ela; flicker mínimo no meio do gesto. Restaura sempre (finally).

using System.Windows.Media;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

internal sealed class ScreenCaptureFlow(OverlayWindow overlay, ToolbarWindow toolbar) : IScreenCaptureFlow
{
    public async Task<CapturedImage?> CaptureRegionDipAsync(RectD region)
    {
        toolbar.Hide();
        try
        {
            await Task.Delay(80);
            var dpi = VisualTreeHelper.GetDpi(overlay);
            int l = (int)Math.Round((overlay.Left + region.X) * dpi.DpiScaleX);
            int t = (int)Math.Round((overlay.Top + region.Y) * dpi.DpiScaleY);
            int w = Math.Max(1, (int)Math.Round(region.Width * dpi.DpiScaleX));
            int h = Math.Max(1, (int)Math.Round(region.Height * dpi.DpiScaleY));
            return await Task.Run(() => ScreenCapture.CaptureRegionPx(l, t, w, h));
        }
        finally
        {
            toolbar.Show();
        }
    }
}
