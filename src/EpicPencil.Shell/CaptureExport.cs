// Exportação da captura atual (Ctrl+C copia, Ctrl+S salva PNG).
// Pipeline (sem chrome de seleção, COM a tinta do canvas):
//   1. Região = bounds ATUAIS da seleção (ou tela virtual completa sem seleção).
//   2. BitBlt one-shot sob demanda da região. O overlay layered nunca entra no
//      BitBlt (sem CAPTUREBLT) — por isso nem o tracejado vermelho nem a tinta
//      aparecem nos pixels crus.
//   3. Os strokes do documento que cruzam a região são renderizados do MODELO
//      sobre o bitmap (coords locais = global − origem do BitBlt).
// Sem captura contínua em background (REQ6): tudo acontece no gesto do usuário.
// Multi-monitor/DPI (REQ5): região e strokes em pixels físicos globais; o
// BitBlt recebe coords virtuais (negativas OK), sem matemática de DPI.

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using EpicPencil.Core;
using EpicPencil.Windows;
using Microsoft.Win32;

namespace EpicPencil.Shell;

internal static class CaptureExport
{
    public static ScreenObject? TryGetSelectedScreen(AppState state)
    {
        if (state.SelectedScreenId is not int id) return null;
        return state.Screens.FirstOrDefault(s => s.Id == id);
    }

    // Tela inteira = união dos BOUNDS físicos (inclui taskbar: o usuário espera
    // a tela completa no Ctrl+C/V, não só a work area do overlay).
    public static RectD GetFullVirtualRect(IReadOnlyList<MonitorInfo> layout)
    {
        if (layout.Count == 0) throw new ArgumentException("layout vazio", nameof(layout));
        int l = layout.Min(m => m.BoundsX);
        int t = layout.Min(m => m.BoundsY);
        int r = layout.Max(m => m.BoundsX + m.BoundsW);
        int b = layout.Max(m => m.BoundsY + m.BoundsH);
        return new RectD(l, t, r - l, b - t);
    }

    public static BitmapSource ToBitmapSource(byte[] bgra, int pixelWidth, int pixelHeight) =>
        WpfStrokeRenderer.CreateBitmap(bgra, pixelWidth, pixelHeight) as BitmapSource
        ?? throw new InvalidOperationException("conversão BGRA→BitmapSource falhou");

    // Arredondamento idêntico ao do ScreenCaptureFlow: a origem usada no
    // mapeamento dos strokes precisa coincidir EXATAMENTE com a do BitBlt
    // (Math.Round determinístico nos mesmos floats → mesmos ints).
    public static (int L, int T, int W, int H) SnapToPixels(RectD globalPx) =>
    (
        (int)Math.Round(globalPx.X),
        (int)Math.Round(globalPx.Y),
        Math.Max(1, (int)Math.Round(globalPx.Width)),
        Math.Max(1, (int)Math.Round(globalPx.Height))
    );

    // Compõe o modelo sobre o bitmap capturado (z-order da tela: capturas,
    // depois tinta, formas e textos). Retorna a base intacta (fast path) quando
    // nada cruza a região. Nunca desenha chrome de seleção — só pixels da tela
    // + conteúdo do modelo. Formas entram como contorno vetorial (REQ10), sem
    // rasterizar nada no Document.
    public static BitmapSource CompositeModel(
        BitmapSource captured, int originX, int originY,
        IReadOnlyList<Stroke> strokes,
        IReadOnlyList<ScreenObject>? screens = null,
        IReadOnlyList<TextObject>? texts = null,
        IReadOnlyList<RectangleObject>? rectangles = null,
        IReadOnlyList<CircleObject>? circles = null)
    {
        var region = new RectD(originX, originY, captured.PixelWidth, captured.PixelHeight);
        List<Stroke>? hit = null;
        foreach (var s in strokes)
            if (s.Bounds.Intersects(region))
                (hit ??= new List<Stroke>()).Add(s);
        List<ScreenObject>? hitScreens = null;
        if (screens is not null)
            foreach (var s in screens)
            {
                var b = new RectD(s.X, s.Y, s.WidthPx, s.HeightPx);
                if (b.Intersects(region))
                    (hitScreens ??= new List<ScreenObject>()).Add(s);
            }
        List<TextObject>? hitTexts = null;
        if (texts is not null)
            foreach (var t in texts)
                if (t.Bounds.Intersects(region))
                    (hitTexts ??= new List<TextObject>()).Add(t);
        List<RectangleObject>? hitRects = null;
        if (rectangles is not null)
            foreach (var r in rectangles)
                if (r.Bounds.Intersects(region))
                    (hitRects ??= new List<RectangleObject>()).Add(r);
        List<CircleObject>? hitCircles = null;
        if (circles is not null)
            foreach (var c in circles)
                if (c.Bounds.Intersects(region))
                    (hitCircles ??= new List<CircleObject>()).Add(c);
        if (hit is null && hitScreens is null && hitTexts is null
            && hitRects is null && hitCircles is null) return captured;

        var root = new System.Windows.Media.DrawingVisual();
        using (var dc = root.RenderOpen())
        {
            dc.DrawImage(captured, new Rect(0, 0, captured.PixelWidth, captured.PixelHeight));
            if (hitScreens is not null)
                foreach (var s in hitScreens)
                    dc.DrawImage(ToBitmapSource(s.Bgra, s.PixelWidth, s.PixelHeight),
                        new Rect(s.X - originX, s.Y - originY, s.WidthPx, s.HeightPx));
            if (hit is not null)
                foreach (var s in hit)
                {
                    var local = new List<Pt>(s.Points.Count);
                    foreach (var p in s.Points)
                        local.Add(new Pt(p.X - originX, p.Y - originY));
                    WpfStrokeRenderer.RenderStrokeInto(dc, local, s.Color, s.WidthPx, s.Tool, s.Opacity);
                }
            // Formas: contorno na origem local (global − origem do BitBlt).
            // Ordem aproximada (screens → strokes → formas → textos); o
            // z-order exato entre tipos nasce do Id de criação no overlay.
            if (hitRects is not null)
                foreach (var r in hitRects)
                    WpfStrokeRenderer.RenderShapeInto(dc, ToolKind.Rectangle,
                        new RectD(r.X - originX, r.Y - originY, r.WidthPx, r.HeightPx),
                        r.Color, r.StrokeWidthPx);
            if (hitCircles is not null)
                foreach (var c in hitCircles)
                    WpfStrokeRenderer.RenderShapeInto(dc, ToolKind.Circle,
                        new RectD(c.X - originX, c.Y - originY, c.WidthPx, c.HeightPx),
                        c.Color, c.StrokeWidthPx);
            if (hitTexts is not null)
                foreach (var t in hitTexts)
                    dc.DrawText(
                        WpfStrokeRenderer.BuildFormattedText(t.Content, t.FontFamily,
                            t.FontSizePx, t.Color, 1.0),
                        new Point(t.X - originX, t.Y - originY));
        }
        var rtb = new RenderTargetBitmap(captured.PixelWidth, captured.PixelHeight,
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    // Clipboard do Windows (STA — chamado na thread da UI): a imagem fica
    // disponível p/ Ctrl+V no Paint/Word/Discord. Não toca na seleção.
    public static void CopyToClipboard(BitmapSource bitmap)
    {
        Clipboard.SetImage(bitmap);
        Log.Info($"captura copiada {bitmap.PixelWidth}x{bitmap.PixelHeight}px");
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    // Diálogo padrão do Windows; OverwritePrompt impede sobrescrita silenciosa.
    // Retorna false quando o usuário cancela.
    public static bool SaveWithDialog(Window owner, BitmapSource bitmap, string defaultName)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Imagem PNG (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = defaultName,
            OverwritePrompt = true,
            Title = "Salvar captura",
        };
        if (dlg.ShowDialog(owner) != true)
        {
            Log.Info("salvamento cancelado pelo usuário");
            return false;
        }
        File.WriteAllBytes(dlg.FileName, EncodePng(bitmap));
        Log.Info($"captura salva {bitmap.PixelWidth}x{bitmap.PixelHeight}px em {dlg.FileName}");
        return true;
    }
}
