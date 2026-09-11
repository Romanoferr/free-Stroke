// Self-test headless da Shell (sem HWND visível: DrawingVisual renderiza
// offscreen na thread STA). RunSync: strokes/undo/erase/hit-test. RunAsync:
// capturas (flow fake). RunWindows: janelas reais breves (overlay+toolbar).

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EpicPencil.Core;
using EpicPencil.Windows;

namespace EpicPencil.Shell;

internal static class ShellSelfTest
{
    // Flow fake: bytes sintéticos (sem tela real) p/ testar marquee→modelo→visual.
    // Registra a última região pedida (em px globais) p/ provar a conversão.
    private sealed class FakeFlow : IScreenCaptureFlow
    {
        public RectD LastRegion;
        public Task<CapturedImage?> CaptureRegionGlobalPxAsync(RectD globalPx)
        {
            LastRegion = globalPx;
            return Task.FromResult<CapturedImage?>(new CapturedImage(8, 6, new byte[4 * 8 * 6]));
        }
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
        RunMonitors(Check);
        RunRebuild(Check);
        RunToolbarUI(Check);
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
        // REQ1–REQ3: regiões. Overlay cobre a WORK AREA DO SEU MONITOR (nunca a
        // taskbar) e a toolbar é owned (sempre acima do overlay primário).
        // Janelas reais: teste breve e invisível (overlay transparente).
        var layout = MonitorLayout.Enumerate();
        Check(layout.Count >= 1, $"enumera monitores (n={layout.Count})");
        var state2 = new AppState();
        var overlays = layout.Select(m => new OverlayWindow(state2, m)).ToList();
        var primary = overlays.FirstOrDefault(o => o.Monitor.IsPrimary) ?? overlays[0];
        var toolbar = new ToolbarWindow(state2, overlays, primary);
        foreach (var overlay in overlays) overlay.Show();
        toolbar.Owner = primary; // WPF: dono precisa estar exibido antes
        toolbar.Show();
        primary.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        var work = System.Windows.SystemParameters.WorkArea;
        Check(Math.Abs(primary.Left - work.Left) < 1 && Math.Abs(primary.Top - work.Top) < 1 &&
            Math.Abs(primary.Width - work.Width) < 1 && Math.Abs(primary.Height - work.Height) < 1,
            $"overlay primário = work area ({primary.Width:F0}x{primary.Height:F0}@{primary.Left:F0},{primary.Top:F0})");
        Check(ReferenceEquals(toolbar.Owner, primary), "toolbar owned (sempre acima do overlay)");
        Check(overlays.All(o => o.IsVisible) && toolbar.IsVisible, $"overlay(s)+toolbar visíveis ({overlays.Count})");
        toolbar.SuppressCloseShutdown = true;
        toolbar.Close();
        foreach (var overlay in overlays) overlay.SuppressCloseShutdown = true;
        foreach (var overlay in overlays) overlay.Close();
    }

    private static void RunMonitors(Action<bool, string> Check)
    {
        // Enumeração Win32: quantidade, primário único, áreas positivas, Ids.
        var layout = MonitorLayout.Enumerate();
        Check(layout.Count == OverlayBehavior.MonitorCount(),
            $"enumerate == SM_CMONITORS ({layout.Count})");
        Check(layout.Count(m => m.IsPrimary) == 1, "exatamente 1 primário");
        Check(layout.All(m => m.WorkW > 0 && m.WorkH > 0), "work areas positivas");
        Check(layout.Select(m => m.Id).SequenceEqual(Enumerable.Range(0, layout.Count)),
            "Ids estáveis 0..n-1 (0 = primário)");
        Console.WriteLine("monitores: " + string.Join(" ",
            layout.Select(m => $"[#{m.Id} {m.DeviceName} {m.WorkW}x{m.WorkH}@({m.WorkX},{m.WorkY}) prim={m.IsPrimary} dpi={m.DpiScaleX:F2}]")));

        // Seleção por ponto: MonitorFromPoint respeita coords negativas.
        foreach (var m in layout)
        {
            bool ok = MonitorLayout.TryFindForPoint(m.WorkX + 2, m.WorkY + 2, layout, out var found);
            Check(ok && found is not null && found.Hmon == m.Hmon,
                $"MonitorFromPoint em ({m.WorkX + 2},{m.WorkY + 2}) → mon#{m.Id}");
        }

        // Documento global compartilhado: stroke desenhado no overlay com
        // origem negativa commita em px globais; o outro overlay converte de volta.
        var state = new AppState();
        var surfA = new InkSurface(); // frame identidade (monitor em 0,0)
        var surfB = new InkSurface(); // monitor à esquerda, origem negativa
        surfB.Frame = new MonitorFrame(-1920, 160, 1, 1);
        surfA.Attach(state);
        surfB.Attach(state);
        surfB.SimulateStroke(new List<Pt> { new(100, 100), new(200, 100), new(300, 150) });
        Check(state.StrokeCount == 1, "stroke do monitor secundário commita");
        var pts = state.Strokes[^1].Points;
        Check(Math.Abs(pts[0].X - (100 - 1920)) < 0.01 && Math.Abs(pts[0].Y - (100 + 160)) < 0.01,
            $"stroke em global negativo ({pts[0].X:F0},{pts[0].Y:F0})");
        var backToA = surfA.Frame.ToLocal(pts[0]);
        Check(Math.Abs(backToA.X - (100 - 1920)) < 0.01, "overlay primário enxerga o stroke global");
        var backToB = surfB.Frame.ToLocal(pts[0]);
        Check(Math.Abs(backToB.X - 100) < 0.01 && Math.Abs(backToB.Y - 100) < 0.01,
            "round-trip global→local do monitor de origem é identidade");

        // Marquee no monitor negativo: flow recebe px globais (BitBlt-ready).
        var flow = new FakeFlow();
        surfB.CaptureFlow = flow;
        surfB.SimulateMarqueeAsync(new RectD(100, 100, 200, 150)).GetAwaiter().GetResult();
        Check(state.ScreenCount == 1, "captura do monitor secundário commita");
        Check(Math.Abs(flow.LastRegion.X - (100 - 1920)) < 0.01 &&
            Math.Abs(flow.LastRegion.Y - (100 + 160)) < 0.01,
            $"flow recebeu global negativo ({flow.LastRegion.X:F0},{flow.LastRegion.Y:F0})");
        Check(Math.Abs(state.Screens[^1].X - flow.LastRegion.X) < 0.01,
            "ScreenObject guarda posição global");

        // BitBlt real na work area não-primária (só com 2+ monitores físicos).
        var secondary = layout.FirstOrDefault(m => !m.IsPrimary);
        if (secondary is not null)
        {
            var real = ScreenCapture.CaptureRegionPx(secondary.WorkX, secondary.WorkY, 32, 32);
            Check(real is not null && real.Bgra.Length == 4 * 32 * 32,
                $"BitBlt real 32x32 no monitor#{secondary.Id} ({secondary.WorkX},{secondary.WorkY})");
        }
        else
        {
            Console.WriteLine("SKIP BitBlt secundário (1 monitor físico)");
        }
    }

    private static void RunRebuild(Action<bool, string> Check)
    {
        // Rebuild de topologia (plug/unplug): solta a toolbar (Owner=null,
        // oculta), fecha overlays com suppress, recria e reanexa. A toolbar
        // SOBREVIVE (sem cascata de owned-windows → sem Shutdown) e as
        // superfícies fechadas param de renderizar (Detach).
        var state = new AppState();
        var layout = MonitorLayout.Enumerate();
        var overlays = layout.Select(m => new OverlayWindow(state, m)).ToList();
        var toolbar = new ToolbarWindow(state, overlays, overlays[0]);
        foreach (var o in overlays) o.Show();
        toolbar.Owner = overlays[0];
        toolbar.Show();
        toolbar.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        Check(overlays.All(o => o.IsVisible), $"rebuild: {overlays.Count} overlay(s) inicial(is)");

        toolbar.Hide();
        toolbar.Owner = null; // ordem obrigatória: Owner só troca oculta
        foreach (var o in overlays) o.SuppressCloseShutdown = true;
        foreach (var o in overlays) o.Close();
        bool toolbarAlive = System.Windows.Application.Current.Windows
            .Cast<System.Windows.Window>().Contains(toolbar);
        Check(toolbarAlive, "rebuild: toolbar sobrevive (sem cascata owned→Shutdown)");
        Check(overlays.All(o => !o.IsVisible), "rebuild: overlays antigos fechados");

        var overlays2 = layout.Select(m => new OverlayWindow(state, m)).ToList();
        toolbar.Retarget(overlays2, overlays2[0]);
        foreach (var o in overlays2) o.Show();
        toolbar.Owner = overlays2[0];
        toolbar.Show();
        Check(overlays2.All(o => o.IsVisible) && toolbar.IsVisible, "rebuild: overlays recriados + toolbar visível");

        // Detach isolado: superfície sem Closed — Attach, Detach explícito,
        // re-Attach duplo (idempotência: sem assinatura dupla).
        var st2 = new AppState();
        var sA = new InkSurface();
        sA.Attach(st2);
        sA.SimulateStroke(new List<Pt> { new(10, 10), new(20, 20) });
        int v1 = ((ContainerVisual)VisualTreeHelper.GetChild(sA, 2)).Children.Count;
        sA.Detach();
        st2.AddFreehand(new List<Pt> { new Pt(50, 50), new Pt(60, 60) }, 4f);
        int v2 = ((ContainerVisual)VisualTreeHelper.GetChild(sA, 2)).Children.Count;
        Check(v1 == 1 && v2 == 1, $"Detach congela superfície ({v1}→{v2})");
        sA.Attach(st2); // replay: 2 strokes do doc → 2 visuals
        int v3 = ((ContainerVisual)VisualTreeHelper.GetChild(sA, 2)).Children.Count;
        sA.Attach(st2); // 2× não duplica (clear + replay, sem assinatura dupla)
        int v4 = ((ContainerVisual)VisualTreeHelper.GetChild(sA, 2)).Children.Count;
        st2.AddFreehand(new List<Pt> { new Pt(70, 70), new Pt(80, 80) }, 4f);
        int v5 = ((ContainerVisual)VisualTreeHelper.GetChild(sA, 2)).Children.Count;
        Check(v3 == 2 && v4 == 2 && v5 == 3, $"Attach replay+idempotente+incremental ({v3},{v4},{v5})");

        // Rebuild com tinta existente (cenário WM_DISPLAYCHANGE real): os novos
        // overlays re-renderizam os strokes do documento compartilhado.
        state.AddFreehand(new List<Pt> { new Pt(10, 10), new Pt(20, 20) }, 4f);
        toolbar.Hide();
        toolbar.Owner = null;
        foreach (var o in overlays2) { o.SuppressCloseShutdown = true; o.Close(); }
        var overlays3 = layout.Select(m => new OverlayWindow(state, m)).ToList();
        toolbar.Retarget(overlays3, overlays3[0]);
        foreach (var o in overlays3) o.Show();
        toolbar.Owner = overlays3[0];
        toolbar.Show();
        bool inkKept = overlays3.All(o =>
            ((ContainerVisual)VisualTreeHelper.GetChild(o.SurfaceControl, 2)).Children.Count == 1);
        Check(inkKept, "rebuild preserva tinta visível (replay 1 stroke/overlay)");

        toolbar.SuppressCloseShutdown = true;
        toolbar.Close();
        foreach (var o in overlays3) { o.SuppressCloseShutdown = true; o.Close(); }
    }

    private static void RunToolbarUI(Action<bool, string> Check)
    {
        // UI nova (só apresentação): expandida por padrão, selected segue a
        // ferramenta, collapse troca os painéis, ícones têm tooltip, swatches=6.
        // Animação (~130 ms) não é aguardada aqui — só a máquina de estados.
        var state = new AppState();
        var layout = MonitorLayout.Enumerate();
        var overlays = layout.Select(m => new OverlayWindow(state, m)).ToList();
        var toolbar = new ToolbarWindow(state, overlays, overlays[0]);
        foreach (var o in overlays) o.Show();
        toolbar.Owner = overlays[0];
        toolbar.Show();
        toolbar.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        Check(!toolbar.IsCollapsed && toolbar.ExpandedPanel.Visibility == Visibility.Visible,
            "toolbar inicia expandida");
        Check(toolbar.SizeToContent == SizeToContent.Manual && toolbar.Width > 200,
            $"tamanho expandido congelado ({toolbar.Width:F0}x{toolbar.Height:F0})");
        double w0 = toolbar.Width;
        Check(toolbar.ColorRow.Children.Count == 6, "6 swatches de cor");

        state.SetTool(ToolKind.EraserStroke);
        Check(!ReferenceEquals(toolbar.EraserButton.Background, Brushes.Transparent) &&
            ReferenceEquals(toolbar.PenButton.Background, Brushes.Transparent),
            "selected visual segue a ferramenta ativa");
        Check(toolbar.PenButton.ToolTip is string tip && tip.Contains("(P)"),
            "botão-ícone tem tooltip com atalho");
        Check(toolbar.ThicknessSlider.Minimum == 1 && toolbar.ThicknessSlider.Maximum == 10,
            "slider de espessura 1–10");
        Check(toolbar.DiagnosticsRow.Visibility == Visibility.Collapsed,
            "diagnósticos ocultos (produção)");

        // Mostrar/ocultar unificado: visível = desenhar, oculto = interagir.
        Check(state.InkVisible && state.IsDrawMode, "estado inicial: desenho visível");
        toolbar.ThicknessSlider.Value = 7;
        Check(state.ActiveWidth == 7, $"slider ajusta espessura (w={state.ActiveWidth:F0})");
        toolbar.HideButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(!state.InkVisible && !state.IsDrawMode, "olho oculta e libera interação");
        toolbar.HideButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(state.InkVisible && state.IsDrawMode, "olho mostra e volta a desenhar");

        toolbar.CollapseButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(toolbar.IsCollapsed && toolbar.CollapsedBar.Visibility == Visibility.Visible &&
            toolbar.ExpandedPanel.Visibility == Visibility.Collapsed,
            "recolher troca p/ barra compacta");
        PumpUntilIdle(toolbar); // animação (~130 ms wall-clock) conclui com a fila bombeada
        toolbar.ExpandButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        PumpUntilIdle(toolbar);
        Check(!toolbar.IsCollapsed && toolbar.ExpandedPanel.Visibility == Visibility.Visible &&
            Math.Abs(toolbar.Width - w0) < 2,
            $"expandir restaura o painel e o tamanho ({toolbar.Width:F0}px)");

        // Hotkey com a barra recolhida: PgUp alterna mostrar/interagir.
        toolbar.CollapseButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        PumpUntilIdle(toolbar);
        SendKey(toolbar, Key.PageUp);
        Check(!state.InkVisible && !state.IsDrawMode, "PgUp recolhida oculta e libera interação");
        toolbar.CollapsedModeButton.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(state.InkVisible && state.IsDrawMode, "dot verde/laranja recolhido mostra e desenha");
        SendKey(toolbar, Key.PageUp);
        Check(!state.InkVisible && !state.IsDrawMode, "PgUp alterna de volta");

        toolbar.SuppressCloseShutdown = true;
        toolbar.Close();
        foreach (var o in overlays) { o.SuppressCloseShutdown = true; o.Close(); }
    }

    // Bombeia a fila do dispatcher por ms reais (timers de UI disparam).
    private static void PumpMs(Window window, int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var done = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ms)
        };
        done.Tick += (_, _) => { done.Stop(); frame.Continue = false; };
        done.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    // Bombeia até a animação da toolbar concluir (teto 2 s — nunca trava o teste).
    private static void PumpUntilIdle(ToolbarWindow toolbar)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (toolbar.IsAnimating && sw.Elapsed.TotalMilliseconds < 2000)
            PumpMs(toolbar, 50);
    }

    // Tecla sintética p/ validar hotkeys locais (toolbar expandida ou recolhida).
    private static void SendKey(Window window, System.Windows.Input.Key key)
    {
        var args = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window), 0, key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
        };
        window.RaiseEvent(args);
    }
}