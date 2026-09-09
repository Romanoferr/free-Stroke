// Definição PURA do job de export (sem SkiaSharp aqui).
// O backend Skia (lazy-loaded, fora do startup) consome este job e implementa
// a mesma StrokeSpec — paridade pixel é aceite do spike S6.

namespace EpicPencil.Core;

public enum ExportBackground
{
    Transparent, // Único suportado no MVP (overlay puro, sem captura de tela)
    White,
    Black
}

public sealed class ExportJob
{
    public RectD RegionDip { get; init; }
    public float Scale { get; init; } = 1f; // 1x = DIPs; 2x para saída 4K/HiDPI
    public ExportBackground Background { get; init; } = ExportBackground.Transparent;

    public void Validate()
    {
        if (Scale is < 0.5f or > 4f) throw new ArgumentOutOfRangeException(nameof(Scale));
        if (RegionDip.Width <= 0 || RegionDip.Height <= 0)
            throw new ArgumentException("Região de export inválida.");
    }
}
