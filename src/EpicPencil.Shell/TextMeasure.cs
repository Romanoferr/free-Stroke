// Medida real do texto na fonte/tamanho (px, pdd=1): gravada no TextObject
// para hit-test (seleção/borracha) e export usarem bounds reais sem WPF no Core.

using EpicPencil.Core;

namespace EpicPencil.Shell;

internal static class TextMeasure
{
    public static (float W, float H) Measure(string content, string familyName, float sizePx)
    {
        var ft = WpfStrokeRenderer.BuildFormattedText(
            content, familyName, sizePx, new Rgba(0, 0, 0), 1.0);
        return ((float)ft.Width, (float)ft.Height);
    }
}
