// Self-test headless da Shell (sem HWND visível: DrawingVisual renderiza
// offscreen na thread STA). Aceite do incremento: 500 strokes sintéticos,
// undo/redo/erase consistentes e p50 de processamento < 3 ms.

using EpicPencil.Core;

namespace EpicPencil.Shell;

internal static class ShellSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }

        var state = new AppState();
        var surface = new InkSurface();
        surface.Attach(state);

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

        Console.WriteLine(failures.Count == 0 ? "SHELL SELFTEST OK" : $"FALHOU: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }
}
