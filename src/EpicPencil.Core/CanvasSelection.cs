// Camada genérica de seleção de objetos (REQ1/REQ3/REQ10).
// Core puro: sem System.Windows, sem DllImport, sem WPF.
// Um ICanvasObject expõe o mínimo para seleção/movimentação futura:
// identificação, posição (via Bounds), bounds, hit-testing.
// TextObject, ScreenObject, RectangleObject e CircleObject implementam esta
// interface; Stroke/Line/Arrow ficam preparados (Kind) mas ainda NÃO são
// selecionáveis neste incremento.
// O ObjectPicker centraliza o hit-test para nenhum ponto da Shell precisar
// de if Text... else if Screen... espalhado.

namespace EpicPencil.Core;

public enum CanvasObjectKind
{
    Text,
    Screen,
    Stroke, // reservado (strokes seguem não-selecionáveis)
    Line, // reservado (linhas assadas como Stroke seguem não-selecionáveis)
    Arrow, // reservado (setas assadas como Stroke seguem não-selecionáveis)
    Rectangle,
    Circle,
}

public interface ICanvasObject
{
    int Id { get; }
    CanvasObjectKind Kind { get; }
    RectD Bounds { get; }
    bool HitTest(Pt p);
}

public readonly record struct SelectedObject(CanvasObjectKind Kind, int Id);

public static class ObjectPicker
{
    // Pad de clicabilidade do texto (mesmo valor do PickText original).
    public const float TextHitPad = 3f;

    public static bool HitText(TextObject t, Pt p) =>
        new RectD(t.X - TextHitPad, t.Y - TextHitPad,
            t.WidthPx + TextHitPad * 2, t.HeightPx + TextHitPad * 2).Contains(p);

    public static bool HitScreen(ScreenObject s, Pt p) => s.Contains(p);

    // Ordem visual (z-order, REQ7): o InkSurface desenha _screens ABAIXO e
    // _finalized (textos + retângulos + círculos + strokes) ACIMA, com append
    // na ordem de criação. Como os Ids vêm do NextId global do Document, a
    // ordem de criação entre tipos = ordem dos Ids: entre os objetos do
    // _finalized vence o MAIOR Id; qualquer um deles vence qualquer captura.
    // Strokes/Line/Arrow: ignorados (futuro).
    // Limitação herdada: undo de erase reinsere no índice original do modelo
    // mas a Shell re-anexa o visual no fim — após undo a ordem visual pode
    // divergir do Id até o próximo replay (mesmo comportamento de strokes).
    public static SelectedObject? PickTopmost(
        IReadOnlyList<TextObject> texts,
        IReadOnlyList<ScreenObject> screens,
        Pt p) => PickTopmost(texts, Array.Empty<RectangleObject>(),
            Array.Empty<CircleObject>(), screens, p);

    public static SelectedObject? PickTopmost(
        IReadOnlyList<TextObject> texts,
        IReadOnlyList<RectangleObject> rectangles,
        IReadOnlyList<CircleObject> circles,
        IReadOnlyList<ScreenObject> screens,
        Pt p)
    {
        SelectedObject? best = null;
        int bestId = -1;
        for (int i = 0; i < texts.Count; i++)
            if (texts[i].Id > bestId && HitText(texts[i], p))
            { best = new SelectedObject(CanvasObjectKind.Text, texts[i].Id); bestId = texts[i].Id; }
        for (int i = 0; i < rectangles.Count; i++)
            if (rectangles[i].Id > bestId && rectangles[i].HitTest(p))
            { best = new SelectedObject(CanvasObjectKind.Rectangle, rectangles[i].Id); bestId = rectangles[i].Id; }
        for (int i = 0; i < circles.Count; i++)
            if (circles[i].Id > bestId && circles[i].HitTest(p))
            { best = new SelectedObject(CanvasObjectKind.Circle, circles[i].Id); bestId = circles[i].Id; }
        if (best.HasValue) return best;
        for (int i = screens.Count - 1; i >= 0; i--) // capturas: topo = fim da lista
            if (HitScreen(screens[i], p))
                return new SelectedObject(CanvasObjectKind.Screen, screens[i].Id);
        return null;
    }

    public static SelectedObject? PickTopmost(Document doc, Pt p) =>
        PickTopmost(doc.Texts, doc.Rectangles, doc.Circles, doc.Screens, p);
}
