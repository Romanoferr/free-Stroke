// Espaço de coordenadas multi-monitor (matemática pura: sem Win32/WPF).
// Modelo: o Document/AppState vive no espaço GLOBAL (pixels físicos da tela
// virtual do Windows — origem pode ser negativa, ex. monitor à esquerda em
// X=-1920). Cada overlay tem um MonitorFrame: origem da sua work area em px
// global + escala (px físicos por DIP daquela janela). A borda converte:
// input local (DIP) → global na entrada; global → local na renderização.
// Com 1 monitor em 100% o frame é identidade e o comportamento é o atual.

namespace EpicPencil.Core;

public readonly record struct MonitorFrame(float OriginPxX, float OriginPxY, float PxPerDipX = 1f, float PxPerDipY = 1f)
{
    public static readonly MonitorFrame Identity = new(0, 0, 1, 1);

    public Pt ToGlobal(Pt local) => new(
        OriginPxX + local.X * PxPerDipX,
        OriginPxY + local.Y * PxPerDipY);

    public Pt ToLocal(Pt global) => new(
        (global.X - OriginPxX) / PxPerDipX,
        (global.Y - OriginPxY) / PxPerDipY);

    public RectD ToGlobal(RectD local)
    {
        var a = ToGlobal(new Pt(local.X, local.Y));
        var b = ToGlobal(new Pt(local.Right, local.Bottom));
        return new RectD(a.X, a.Y, b.X - a.X, b.Y - a.Y);
    }

    public RectD ToLocal(RectD global)
    {
        var a = ToLocal(new Pt(global.X, global.Y));
        var b = ToLocal(new Pt(global.Right, global.Bottom));
        return new RectD(a.X, a.Y, b.X - a.X, b.Y - a.Y);
    }

    public List<Pt> ToGlobalList(IReadOnlyList<Pt> local)
    {
        var out_ = new List<Pt>(local.Count);
        foreach (var p in local) out_.Add(ToGlobal(p));
        return out_;
    }

    public List<Pt> ToLocalList(IReadOnlyList<Pt> global)
    {
        var out_ = new List<Pt>(global.Count);
        foreach (var p in global) out_.Add(ToLocal(p));
        return out_;
    }

    // O ponto global pertence a este monitor? (w/h em px físicos da work area)
    public bool ContainsGlobal(Pt global, float workWidthPx, float workHeightPx) =>
        global.X >= OriginPxX && global.X < OriginPxX + workWidthPx &&
        global.Y >= OriginPxY && global.Y < OriginPxY + workHeightPx;
}
