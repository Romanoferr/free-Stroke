// Texto como objeto próprio do documento (não é Stroke, não é pixel).
// Posição, tamanho e métricas em PX GLOBAIS da tela virtual (mesmo espaço dos
// strokes); a borda converte p/ DIPs locais na renderização.
// Mutação SOMENTE via comandos (MoveTextCommand/UpdateTextCommand), mesmo
// padrão de ScreenObject.X/Y — nunca direto pela Shell.
// WidthPx/HeightPx = medida da fonte em px (pdd=1), gravada pela Shell na
// criação/edição: o Core segue puro (sem WPF) e o hit-test usa bounds reais.

namespace EpicPencil.Core;

public sealed class TextObject : ICanvasObject
{
    public int Id { get; }
    public CanvasObjectKind Kind => CanvasObjectKind.Text;
    public float X { get; set; } // mutado SOMENTE via MoveTextCommand
    public float Y { get; set; } // mutado SOMENTE via MoveTextCommand
    public string Content { get; internal set; } // via UpdateTextCommand
    public float FontSizePx { get; internal set; } // via UpdateTextCommand
    public string FontFamily { get; }
    public Rgba Color { get; }
    public float WidthPx { get; internal set; } // via UpdateTextCommand
    public float HeightPx { get; internal set; } // via UpdateTextCommand

    public TextObject(int id, float x, float y, string content,
        float fontSizePx, string fontFamily, Rgba color, float widthPx, float heightPx)
    {
        if (string.IsNullOrEmpty(content)) throw new ArgumentException("texto vazio", nameof(content));
        if (fontSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(fontSizePx));
        if (string.IsNullOrWhiteSpace(fontFamily)) throw new ArgumentException("família inválida", nameof(fontFamily));
        if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException("métricas inválidas");
        Id = id;
        X = x; Y = y;
        Content = content;
        FontSizePx = fontSizePx;
        FontFamily = fontFamily;
        Color = color;
        WidthPx = widthPx;
        HeightPx = heightPx;
    }

    public RectD Bounds => new(X, Y, WidthPx, HeightPx);

    public bool HitTest(Pt p) => ObjectPicker.HitText(this, p);
}
