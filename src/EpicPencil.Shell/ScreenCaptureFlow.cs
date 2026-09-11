// Orquestra a captura escondendo a toolbar (janela normal APARECERIA no BitBlt;
// o overlay layered já é excluído sem CAPTUREBLT). 80 ms ≈ 5 frames p/ compor
// sem ela; flicker mínimo no meio do gesto. Restaura sempre (finally).
// Uma instância compartilhada serve todos os overlays (toolbar é única).

using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

internal sealed class ScreenCaptureFlow(ToolbarWindow toolbar) : IScreenCaptureFlow
{
    public async Task<CapturedImage?> CaptureRegionGlobalPxAsync(RectD globalPx)
    {
        toolbar.Hide();
        try
        {
            await Task.Delay(80);
            int l = (int)Math.Round(globalPx.X);
            int t = (int)Math.Round(globalPx.Y);
            int w = Math.Max(1, (int)Math.Round(globalPx.Width));
            int h = Math.Max(1, (int)Math.Round(globalPx.Height));
            return await Task.Run(() => ScreenCapture.CaptureRegionPx(l, t, w, h));
        }
        finally
        {
            toolbar.Show();
        }
    }
}
