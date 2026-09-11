// Abstração de um monitor físico (dados Win32 puros: sem WPF).
// Bounds/Work em PIXELS FÍSICOS da tela virtual (negativos preservados).
// DpiScale = px físicos por DIP (1.0 = 100%). Frame deriva o espaço global.

using EpicPencil.Core;

namespace EpicPencil.Windows;

public sealed record MonitorInfo(
    int Id,
    IntPtr Hmon,
    string DeviceName,
    int BoundsX, int BoundsY, int BoundsW, int BoundsH,
    int WorkX, int WorkY, int WorkW, int WorkH,
    bool IsPrimary,
    double DpiScaleX, double DpiScaleY)
{
    // Origem do overlay = work area (REQ3: taskbar nunca coberta).
    // NOTA: escala da JANELA (VisualTreeHelper.GetDpi, medida no Loaded) é a
    // autoritativa p/ o Frame; DpiScale aqui (GetDpiForMonitor) é o seed inicial.
    public MonitorFrame Frame => new(WorkX, WorkY, (float)DpiScaleX, (float)DpiScaleY);

    public bool ContainsPx(int x, int y) =>
        x >= WorkX && x < WorkX + WorkW && y >= WorkY && y < WorkY + WorkH;
}
