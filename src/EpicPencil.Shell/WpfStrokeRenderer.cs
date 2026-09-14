// ÚNICO backend interativo (DrawingVisual sobre DirectX retido).
// Regras: 1 visual por stroke finalizado (Freeze após commit); stroke ativo é
// 1 visual reconstruído por lote de input; marker compõe opacidade UMA vez
// (nunca carimba segmentos alfa — evita escurecer o overlap).
// O backend de export (Skia, futuro) implementa a mesma StrokeSpec do Core.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EpicPencil.Core;

namespace EpicPencil.Shell;

internal static class WpfStrokeRenderer
{
    private static readonly Dictionary<(uint Color, float Width, float Opacity, ToolKind Tool), Pen> PenCache = new();
    private static Pen? _marqueePen;

    // Retângulo de seleção tracejado (marquee + highlight da captura ativa).
    public static void RenderMarquee(DrawingVisual visual, RectD rect)
    {
        _marqueePen ??= CreateMarqueePen();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(null, _marqueePen,
            new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height));
    }

    private static Pen CreateMarqueePen()
    {
        var brush = new SolidColorBrush(Colors.Red);
        brush.Freeze();
        var pen = new Pen(brush, 1.5) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    // Bitmap imutável da captura (conversão BGRA→WPF uma única vez; move usa Offset).
    public static ImageSource CreateBitmap(byte[] bgra, int pixelWidth, int pixelHeight)
    {
        var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96,
            PixelFormats.Bgra32, null, bgra, 4 * pixelWidth);
        bmp.Freeze();
        return bmp;
    }

    // Texto finalizado: FormattedText (nítido em qualquer DPI via pixelsPerDip),
    // mesma família da edição. emSize em DIPs na tela, em px na exportação.
    public static FormattedText BuildFormattedText(
        string text, string familyName, double emSize, Rgba color, double pixelsPerDip)
    {
        var typeface = new Typeface(TextFonts.ResolveFamily(familyName),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, emSize,
            new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)),
            pixelsPerDip);
    }

    // Visual do texto no overlay: posição/tamanho locais (DIP) deste monitor.
    public static DrawingVisual BuildTextVisual(Pt localPos, double dipSize,
        string familyName, Rgba color, string text, double pixelsPerDip)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawText(BuildFormattedText(text, familyName, dipSize, color, pixelsPerDip),
                new System.Windows.Point(localPos.X, localPos.Y));
        return visual;
    }

    // Visual da captura: imagem em px, posicionada via Offset local (DIP).
    // Mover = trocar Offset, sem re-render. Rect local explícito
    // (multi-monitor: cada overlay converte os px globais p/ seu DIP).
    public static DrawingVisual BuildScreenVisual(ImageSource image, RectD localRect) =>
        BuildScreenVisual(image, localRect.Width, localRect.Height, localRect.X, localRect.Y);

    public static DrawingVisual BuildScreenVisual(ImageSource image, float widthDip, float heightDip, float offsetX, float offsetY)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(image, new System.Windows.Rect(0, 0, widthDip, heightDip));
        visual.Offset = new Vector(offsetX, offsetY);
        return visual;
    }

    // Formas geométricas (somente contorno, sem preenchimento): geometria
    // desenhada em (0,0,W,H) + Offset=(X,Y) — mover = trocar Offset, sem
    // re-render (mesmo padrão da captura). Rect/pontos em DIP local.
    public static DrawingVisual BuildRectangleVisual(RectD localRect, Rgba color, float widthDip)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            RenderShapeInto(dc, ToolKind.Rectangle,
                new RectD(0, 0, localRect.Width, localRect.Height), color, widthDip);
        visual.Offset = new Vector(localRect.X, localRect.Y);
        return visual;
    }

    public static DrawingVisual BuildCircleVisual(RectD localRect, Rgba color, float widthDip)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            RenderShapeInto(dc, ToolKind.Circle,
                new RectD(0, 0, localRect.Width, localRect.Height), color, widthDip);
        visual.Offset = new Vector(localRect.X, localRect.Y);
        return visual;
    }

    // Preview da forma durante o drag: reconstrói 1 visual pequeno por move
    // (sem objeto no Document, sem histórico — REQ13).
    public static void RenderShapePreview(DrawingVisual active, ToolKind tool,
        RectD localRect, Rgba color, float widthDip)
    {
        using var dc = active.RenderOpen();
        RenderShapeInto(dc, tool, localRect, color, widthDip);
    }

    // Núcleo reutilizável num DrawingContext arbitrário (preview interativo
    // + visual finalizado + exportação Ctrl+C/S — nunca desenha seleção).
    public static void RenderShapeInto(DrawingContext dc, ToolKind tool,
        RectD localRect, Rgba color, float width)
    {
        var pen = GetPen(color, width, 1f, tool);
        var rect = new System.Windows.Rect(localRect.X, localRect.Y, localRect.Width, localRect.Height);
        if (tool == ToolKind.Circle)
            dc.DrawEllipse(null, pen,
                new System.Windows.Point(localRect.X + localRect.Width / 2,
                    localRect.Y + localRect.Height / 2),
                localRect.Width / 2, localRect.Height / 2);
        else
            dc.DrawRectangle(null, pen, rect);
    }

    // Overload com pontos já convertidos p/ o frame local do overlay
    // (multi-monitor: o stroke vive em px globais no modelo; a largura chega
    // em DIP local = px ÷ escala). Sem overload direto de Stroke de propósito:
    // passar o modelo global sem converter reintroduziria o bug de 1 monitor.
    public static DrawingVisual BuildStrokeVisual(IReadOnlyList<Pt> points,
        Rgba color, float width, ToolKind tool, float opacity)
    {
        var visual = new DrawingVisual();
        RenderInto(visual, points, color, width, tool, opacity);
        // Sem Freeze: DrawingVisual não é Freezable. A imutabilidade vem de
        // nunca reabrir o visual + recursos internos (Pen/Brush/Geometry) congelados.
        return visual;
    }

    public static void RenderPreview(DrawingVisual active, IReadOnlyList<Pt> points,
        Rgba color, float width, ToolKind tool, float opacity) =>
        RenderInto(active, points, color, width, tool, opacity);

    private static void RenderInto(DrawingVisual visual, IReadOnlyList<Pt> points,
        Rgba color, float width, ToolKind tool, float opacity)
    {
        using var dc = visual.RenderOpen();
        RenderStrokeInto(dc, points, color, width, tool, opacity);
    }

    // Núcleo de desenho reutilizável num DrawingContext arbitrário (exportação
    // compõe os strokes sobre o bitmap capturado; nunca desenha chrome de seleção).
    public static void RenderStrokeInto(DrawingContext dc, IReadOnlyList<Pt> points,
        Rgba color, float width, ToolKind tool, float opacity)
    {
        var pen = GetPen(color, width, opacity, tool);
        if (points.Count == 1)
        {
            var p = new System.Windows.Point(points[0].X, points[0].Y);
            dc.DrawLine(pen, p, p); // round cap => ponto vira disco
            return;
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new System.Windows.Point(points[0].X, points[0].Y), false, false);
            var rest = new PointCollection(points.Count - 1);
            for (int i = 1; i < points.Count; i++)
                rest.Add(new System.Windows.Point(points[i].X, points[i].Y));
            ctx.PolyLineTo(rest, true, true);
        }
        geo.Freeze();
        if (opacity < 1f)
        {
            dc.PushOpacity(opacity);
            dc.DrawGeometry(null, pen, geo);
            dc.Pop();
        }
        else
        {
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static Pen GetPen(Rgba color, float width, float opacity, ToolKind tool)
    {
        uint key = ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | color.A;
        var cacheKey = (key, width, opacity, tool);
        if (!PenCache.TryGetValue(cacheKey, out var pen))
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            // Largura com alfa usa geometria opaca + PushOpacity (composição única).
            pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            PenCache[cacheKey] = pen;
        }
        return pen;
    }
}
