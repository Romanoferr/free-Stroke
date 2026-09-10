// Self-test headless da Shell (sem HWND visível: DrawingVisual renderiza
// offscreen na thread STA). RunSync: strokes/undo/erase/hit-test. RunAsync:
// capturas (flow fake). RunWindows: janelas reais breves (overlay+toolbar).

using System.Windows;
using System.Windows.Media;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

internal static class ShellSelfTest
{
    // Flow fake: bytes sintéticos (sem tela real) p/ testar marquee→modelo→visual.
    private sealed class FakeFlow : IScreenCaptureFlow
    {
        public Task<CapturedImage?> CaptureRegionDipAsync(RectD region) =>
            Task.FromResult<CapturedImage?>(new CapturedImage(8, 6, new byte[4 * 8 * 6]));
    }

    public static int Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }
        RunSync(Check);
        RunAsync(Check).GetAwaiter().GetResult();
        RunWindows(Check);
        Console.WriteLine(failures.Count == 0 ? "SHELL SELFTEST OK" : $"FALHOU: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void RunSync(Action<bool, string> Check)
    {
        var state = new AppState();
        var surface = new InkSurface();
        surface.Attach(state);

        // Regressão do bug "linha não sai": canvas VAZIO precisa ser atingível
        // pelo hit-test, senão o StylusDown cai na Window e nada desenha.
        Log.Init();
        surface.Measure(new Size(1920, 1080));
        surface.Arrange(new Rect(0, 0, 1920, 1080));
        var hit = VisualTreeHelper.HitTest(surface, new Point(960, 540));
        Check(hit?.VisualHit == surface, "canvas vazio recebe hit-test (StylusDown chega)");

        // Regressão do bug "cliques atravessam canvas vazio": o HitTest SIMPLES
        // acima chama HitTestCore direto (sem bounds pre-check) e PASSAVA mesmo
        // com o bug. O HitTest COMPLETO (callbacks) reproduz o path do
        // WM_NCHITTEST: sem o retângulo _hit, o canvas vazio vira HTTRANSPARENT.
        var empty = new InkSurface();
        empty.Attach(new AppState());
        empty.Measure(new Size(1920, 1080));
        empty.Arrange(new Rect(0, 0, 1920, 1080));
        Visual? found = null;
        VisualTreeHelper.HitTest(empty, null,
            r => { found = r.VisualHit as Visual; return HitTestResultBehavior.Stop; },
            new PointHitTestParameters(new Point(960, 540)));
        bool owned = false;
        for (var v = found; v != null; v = VisualTreeHelper.GetParent(v) as Visual)
            if (ReferenceEquals(v, empty)) { owned = true; break; }
        Check(owned, "hit-test completo (path WM_NCHITTEST) atinge canvas vazio");
        Check(System.IO.File.Exists(Log.Path), $"log criado em {Log.Path}");

        // Rabisco sintético: espiral de 200 pontos x 500 strokes.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int s = 0; s < 500; s++)
        {
            var pts = new List<Pt>(200);
            for (int i = 0; i < 200; i++)
            {
                float a = i * 0.1f;
                pts.Add(new Pt(100 + s + a * 20 * MathF.Cos(a), 100 + a * 20 * MathF.Sin(a)));
            }
            surface.SimulateStroke(pts);
        }
        sw.Stop();
        Check(state.StrokeCount == 500, $"500 strokes commitados (tem {state.StrokeCount})");
        Console.WriteLine($"commit 500 strokes: {sw.Elapsed.TotalMilliseconds:F0} ms total, " +
            $"p50 preview+commit={surface.ProcessingP50Ms:F2} ms");
        Check(surface.ProcessingP50Ms < 3, "p50 processamento < 3 ms");
        Check(state.PointCount < 500 * 200, $"RDP reduziu pontos ({state.PointCount})");

        // Teto do UndoStack: só os últimos 50 comandos sobrevivem (por design).
        int undone = 0;
        while (state.CanUndo) { state.Undo(); undone++; }
        Check(undone == 50 && state.StrokeCount == 450, $"undo respeita teto de 50 ({undone}, restam {state.StrokeCount})");
        while (state.CanRedo) state.Redo();
        Check(state.StrokeCount == 500, "redo total restaura tudo");

        int erased = state.EraseSegment(new Pt(0, 0), new Pt(4000, 4000));
        Check(erased > 0 && state.StrokeCount == 500 - erased, $"borracha por segmento ({erased})");
        state.Undo();
        Check(state.StrokeCount == 500, "undo da borracha restaura z-order");

        state.Clear();
        Check(state.StrokeCount == 0, "clear esvazia");
        state.Undo();
        Check(state.StrokeCount == 500, "undo do clear restaura");

        // Ferramentas do incremento: marker (opacidade), linha, seta, presets.
        state.Clear();
        while (state.CanUndo) state.Undo(); // esvazia pilha p/ contagem exata
        state.Clear();
        state.SetTool(ToolKind.Highlighter);
        var markerPts = new List<Pt>();
        for (int i = 0; i <= 50; i++) markerPts.Add(new Pt(i * 4, 200));
        surface.SimulateStroke(markerPts);
        Check(state.StrokeCount == 1, "marker commita");

        state.SetTool(ToolKind.Line);
        surface.SimulateShape(ToolKind.Line, new Pt(0, 0), new Pt(300, 100));
        state.SetTool(ToolKind.Arrow);
        surface.SimulateShape(ToolKind.Arrow, new Pt(0, 300), new Pt(300, 400));
        Check(state.StrokeCount == 3, $"linha+seta commitadas ({state.StrokeCount})");

        state.SetTool(ToolKind.Pen);
        state.SetActiveWidthPreset(0);
        float wS = state.ActiveWidth;
        state.SetActiveWidthPreset(2);
        Check(state.ActiveWidth > wS, $"preset S<L ({wS:F1} < {state.ActiveWidth:F1})");
        state.SetTool(ToolKind.Highlighter);
        Check(state.ActiveWidth == StrokeSpec.DefaultWidth(ToolKind.Highlighter),
            "preset memorizado por ferramenta (marker manteve largura)");
        state.ActiveColor = new Rgba(0, 0, 255);
        state.SetTool(ToolKind.Pen);
        state.SetTool(ToolKind.Highlighter);
        Check(state.ActiveColor.Equals(new Rgba(0, 0, 255)), "cor memorizada por ferramenta");
    }

    private static async Task RunAsync(Action<bool, string> Check)
    {
        // REQ1: marquee→captura→move→delete com undo, via flow fake.
        var state = new AppState();
        var surface = new InkSurface();
        surface.Attach(state);
        surface.CaptureFlow = new FakeFlow();
        surface.Measure(new Size(1920, 1080));
        surface.Arrange(new Rect(0, 0, 1920, 1080));

        await surface.SimulateMarqueeAsync(new RectD(100, 100, 200, 150));
        Check(state.ScreenCount == 1, "marquee cria captura");
        Check(state.SelectedScreenId.HasValue, "captura nova auto-selecionada");
        int id = state.SelectedScreenId!.Value;
        Check(state.Screens.Count == 1 && state.Screens[0].PixelWidth == 8, "bytes do flow preservados");

        Check(state.MoveScreen(id, 300, 300), "move commita");
        Check(state.Screens[0].X == 300 && state.Screens[0].Y == 300, "posição atualizada");
        state.Undo();
        Check(state.Screens[0].X == 100 && state.Screens[0].Y == 100, "undo do move volta à origem");
        state.Redo();
        Check(state.Screens[0].X == 300, "redo do move reaplica");

        Check(state.DeleteSelectedScreen(), "delete remove selecionada");
        Check(state.ScreenCount == 0, "zero capturas após delete");
        state.Undo();
        Check(state.ScreenCount == 1, "undo do delete restaura");

        // Captura GDI real (32x32 no canto): prova BitBlt/GetDIBits sem tela fake.
        // ConfigureAwait(false): o Run roda bloqueado no dispatcher (GetResult).
        var real = await Task.Run(() => ScreenCapture.CaptureRegionPx(0, 0, 32, 32)).ConfigureAwait(false);
        Check(real is not null && real.Bgra.Length == 4 * 32 * 32, "BitBlt real 32x32 funciona");
        Check(ScreenCapture.CaptureRegionPx(0, 0, 99999, 10) is null, "rect absurdo rejeitado");
    }

    private static void RunWindows(Action<bool, string> Check)
    {
        // REQ1–REQ3: regiões. Overlay cobre a WORK AREA (nunca a taskbar) e a
        // toolbar é owned (sempre acima do overlay). Janelas reais: teste breve
        // e invisível (overlay transparente), fechadas em seguida.
        var state2 = new AppState();
        var overlay = new OverlayWindow(state2);
        var toolbar = new ToolbarWindow(state2, overlay);
        overlay.Show();
        toolbar.Owner = overlay; // WPF: dono precisa estar exibido antes
        toolbar.Show();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        var work = System.Windows.SystemParameters.WorkArea;
        Check(Math.Abs(overlay.Left - work.Left) < 1 && Math.Abs(overlay.Top - work.Top) < 1 &&
            Math.Abs(overlay.Width - work.Width) < 1 && Math.Abs(overlay.Height - work.Height) < 1,
            $"overlay = work area ({overlay.Width:F0}x{overlay.Height:F0}@{overlay.Left:F0},{overlay.Top:F0})");
        Check(ReferenceEquals(toolbar.Owner, overlay), "toolbar owned (sempre acima do overlay)");
        Check(overlay.IsVisible && toolbar.IsVisible, "overlay+toolbar visíveis");
        toolbar.Close();
        overlay.Close();
    }
}
