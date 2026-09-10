// ÚNICA especificação de traço do produto (StrokeSpec).
// Os dois backends (WPF DrawingVisual interativo + Skia export) implementam
// esta spec — é o que garante "export == tela" (aceite de paridade do Review).

namespace EpicPencil.Core;

public static class StrokeSpec
{
    // Caps/joins redondos sempre: evita artefatos de pico em curvas rápidas.
    public const float HighlighterOpacity = 0.4f;

    public static float DefaultOpacity(ToolKind tool) => tool switch
    {
        ToolKind.Highlighter => HighlighterOpacity,
        _ => 1f
    };

    public static float DefaultWidth(ToolKind tool) => tool switch
    {
        ToolKind.Pen => 4f,
        ToolKind.Pencil => 2f,
        ToolKind.Highlighter => 18f,
        ToolKind.Line => 4f,
        ToolKind.Arrow => 5f,
        _ => 4f
    };

    // Tolerância de simplificação proporcional à largura: RDP fixo destrói
    // curvas finas ou preserva ruído em marker largo (risco N-10 do Review).
    public static float CommitTolerance(float widthDip) =>
        Math.Clamp(widthDip * 0.08f, 0.75f, 2.5f);

    // Filtro incremental na captura: descarta micro-jitter sem rodar RDP por move.
    public static float CaptureMinDistance(float widthDip) =>
        Math.Clamp(widthDip * 0.15f, 0.6f, 2f);

    // Presets S/M/L por ferramenta (size 0/1/2), relativos ao default.
    public static float WidthPreset(ToolKind tool, int size) => size switch
    {
        0 => DefaultWidth(tool) * 0.5f,
        2 => DefaultWidth(tool) * 2f,
        _ => DefaultWidth(tool)
    };
}
