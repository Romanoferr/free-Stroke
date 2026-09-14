// Texto como objeto próprio do documento (não é Stroke, não é pixel).
// Posição e tamanho em PX GLOBAIS da tela virtual (mesmo espaço dos strokes);
// a borda converte p/ DIPs locais na renderização. X/Y têm setter p/ futura
// movimentação sem reestruturar o modelo; conteúdo/cor/fonte são imutáveis
// (edição futura = remover + recriar, mesmo padrão dos comandos atuais).

namespace EpicPencil.Core;

public sealed class TextObject
{
    public int Id { get; }
    public float X { get; set; } // mutado SOMENTE via comando futuro (padrão MoveScreenCommand)
    public float Y { get; set; }
    public string Content { get; }
    public float FontSizePx { get; } // px globais (tamanho DIP × escala do monitor de origem)
    public string FontFamily { get; } // ex. "Space Mono" (fallback resolvido na Shell)
    public Rgba Color { get; }

    public TextObject(int id, float x, float y, string content,
        float fontSizePx, string fontFamily, Rgba color)
    {
        if (string.IsNullOrEmpty(content)) throw new ArgumentException("texto vazio", nameof(content));
        if (fontSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(fontSizePx));
        if (string.IsNullOrWhiteSpace(fontFamily)) throw new ArgumentException("família inválida", nameof(fontFamily));
        Id = id;
        X = x; Y = y;
        Content = content;
        FontSizePx = fontSizePx;
        FontFamily = fontFamily;
        Color = color;
    }
}
