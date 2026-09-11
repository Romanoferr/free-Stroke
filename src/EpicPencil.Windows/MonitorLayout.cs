// Enumeração de monitores via EnumDisplayMonitors + GetMonitorInfoW.
// Id estável na execução: primário = 0, demais por ordem de dispositivo.
// DPI via GetDpiForMonitor (fallback 96 quando shcore falhar).

namespace EpicPencil.Windows;

public static class MonitorLayout
{
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var hmons = new List<IntPtr>();
        Native.MonitorEnumProc proc = (IntPtr hmon, IntPtr _, ref Native.RECT _, IntPtr _) => { hmons.Add(hmon); return true; };
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);

        var infos = new List<MonitorInfo>();
        foreach (var hmon in hmons)
        {
            var mi = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfoW(hmon, ref mi)) continue;
            var (sx, sy) = DpiScaleFor(hmon);
            infos.Add(new MonitorInfo(
                -1, hmon, mi.szDevice,
                mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                mi.rcWork.Left, mi.rcWork.Top,
                mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top,
                (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0, sx, sy));
        }
        // Primário primeiro (Id 0 = primário, estável p/ toolbar owner);
        // secundários por dispositivo (Id estável entre rebuilds).
        infos.Sort((a, b) =>
        {
            int p = (b.IsPrimary ? 1 : 0).CompareTo(a.IsPrimary ? 1 : 0);
            return p != 0 ? p : string.Compare(a.DeviceName, b.DeviceName, StringComparison.Ordinal);
        });
        return infos.Select((m, i) => m with { Id = i }).ToList();
    }

    public static bool TryFindForPoint(int x, int y, IReadOnlyList<MonitorInfo> layout, out MonitorInfo? monitor)
    {
        monitor = null;
        if (layout.Count == 0) return false;
        IntPtr hmon = Native.MonitorFromPoint(new Native.POINT { X = x, Y = y }, Native.MONITOR_DEFAULTTONEAREST);
        foreach (var m in layout)
            if (m.Hmon == hmon) { monitor = m; return true; }
        // HMONITOR não casou (topologia mudou após enumerate): contém ponto ou primário.
        foreach (var m in layout)
            if (m.ContainsPx(x, y)) { monitor = m; return true; }
        monitor = layout[0];
        return true;
    }

    private static (double Sx, double Sy) DpiScaleFor(IntPtr hmon)
    {
        try
        {
            if (Native.GetDpiForMonitor(hmon, Native.MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0
                && dx >= 48 && dy >= 48)
                return (dx / 96.0, dy / 96.0);
        }
        catch { /* shcore ausente: fallback 96 */ }
        return (1.0, 1.0);
    }
}
