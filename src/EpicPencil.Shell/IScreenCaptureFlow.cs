using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

// Orquestração da captura vive na Shell (precisa da toolbar p/ esconder).
// Implementação real: ScreenCaptureFlow (App). Fake injetável no selftest.
public interface IScreenCaptureFlow
{
    Task<CapturedImage?> CaptureRegionDipAsync(RectD region);
}
