// Exportação da captura atual (Ctrl+C copia, Ctrl+S salva PNG).
// Definição de "seleção atual" (REQ4): o ScreenObject cujo Id ==
// AppState.SelectedScreenId (o mesmo que desenha o tracejado vermelho).
//   Existe seleção? SIM → reutiliza os bytes BGRA do ScreenObject (cópia exata
//     capturada no marquee; mover o objeto muda só X/Y, os pixels seguem
//     intactos — sem re-BitBlt, sem custo, sem erro de DPI).
//   NÃO → BitBlt one-shot sob demanda da TELA VIRTUAL completa (união dos
//     bounds físicos de todos os monitores, coords negativas OK).
// Sem captura contínua em background (REQ6): tudo acontece no gesto do usuário.
// Multi-monitor/DPI (REQ5): os dois caminhos já operam em pixels físicos
// globais — o BitBlt recebe coords virtuais e o objeto guarda px de origem.

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
