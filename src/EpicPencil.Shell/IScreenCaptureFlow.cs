using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

// Orquestração da captura vive na Shell (precisa da toolbar p/ esconder).
// Região em PX GLOBAIS da tela virtual (negativos OK) — BitBlt já opera nesse
// espaço; sem matemática de DPI aqui. Implementação real: ScreenCaptureFlow
// (App). Fake injetável no selftest.
public interface IScreenCaptureFlow
{
    Task<CapturedImage?> CaptureRegionGlobalPxAsync(RectD globalPx);
}
