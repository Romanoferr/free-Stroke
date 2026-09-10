// Captura de região como objeto independente do canvas (REQ1).
// Semântica de CÓPIA: os pixels de origem nunca são alterados; o objeto tem
// posição própria e vive ACIMA do conteúdo original (é tinta do overlay).
// Pixels em bytes BGRA crus (sem tipos UI) para o Core continuar puro;
// o backend de render (Shell) converte para BitmapSource uma única vez.
// Persistência futura: encode PNG desses bytes (fora do MVP).

namespace EpicPencil.Core;

public sealed class ScreenObject
{
    public int Id { get; }
    public float X { get; set; } // mutado SOMENTE via MoveScreenCommand
    public float Y { get; set; }
    public float WidthDip { get; }
    public float HeightDip { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public byte[] Bgra { get; } // 4 * PixelWidth * PixelHeight, BGRA32 top-down

    public ScreenObject(int id, float x, float y, float widthDip, float heightDip,
        int pixelWidth, int pixelHeight, byte[] bgra)
    {
        if (widthDip <= 0 || heightDip <= 0) throw new ArgumentOutOfRangeException("rect DIP inválido");
        if (pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentOutOfRangeException("rect pixel inválido");
        if (bgra.Length != 4 * pixelWidth * pixelHeight) throw new ArgumentException("bytes incompatíveis com WxH");
        Id = id;
        X = x; Y = y;
        WidthDip = widthDip; HeightDip = heightDip;
        PixelWidth = pixelWidth; PixelHeight = pixelHeight;
        Bgra = bgra;
    }

    public RectD Bounds => new(X, Y, WidthDip, HeightDip);
    public bool Contains(Pt p) => Bounds.Contains(p);
    public long ByteSize => Bgra.LongLength;
}
