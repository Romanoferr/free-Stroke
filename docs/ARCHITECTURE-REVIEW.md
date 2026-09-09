# ARCHITECTURE REVIEW — Epic Pencil
**Papel:** Segundo Software Architect revisando o documento `ARCHITECTURE.md v1.0`
**Data:** 2026-09-09 | **Versão:** 1.0 | **Regra:** nenhum código de produção até GO/SPIKE/BLOCK estarem resolvidos.
**Tom:** crítica honesta. O documento original está bem estruturado e acerta na direção (nativo .NET, overlay puro, Core portável), mas contém **3 incoerências arquiteturais, 2 metas fisicamente questionáveis e 1 decisão de hotkey perigosa** que precisam de correção antes dos protótipos.

---

## 1. Executive Summary

**O que está sólido e deve ser mantido:** stack nativa .NET no Windows (rejeição a Electron/Tauri está correta e bem justificada), overlay puro sem captura no MVP, 1 janela por monitor, undo por comandos, settings em JSON, distribuição portátil + Inno Setup, offline-first.

**O que precisa mudar antes de implementar (resumo):**

| # | Decisão original | Veredito do revisor |
|---|------------------|---------------------|
| R1 | Render = "InkCanvas / DrawingVisual + SkiaSharp" misturados | ❌ Incoerente. Escolher **um único caminho interativo: DrawingVisual sobre WPF/DirectX**. InkCanvas fora do caminho crítico; SkiaSharp só para export offscreen |
| R2 | "Repaint parcial via dirty rect controlada pelo app" | ❌ Premissa incorreta sobre WPF. O app não controla dirty rects do DWM; a otimização real é **reter visuals congelados + reconstruir só o stroke ativo** |
| R3 | Zoom + Pan da camada no MVP | ❌ Remover do MVP. Custo alto (coordenadas, input inverso, hit-test, multi-monitor) para valor baixo. Manter `Camera` no modelo mas travada em identidade |
| R4 | `Ctrl+Z` global + `Alt+D` global + `F9/F10` globais | ❌ Perigoso. `Ctrl+Z` global é veto. `Alt+D`, `F9/F10` colidem com navegadores/IDEs. Reduzir para **2–3 globais com duplo modificador** |
| R5 | Watchdog de z-order por polling (500 ms–1 s) | ❌ Conflita com meta CPU idle ~0%. Trocar por **estratégia orientada a eventos + reassert sob demanda** |
| R6 | Metas `< 8 ms` e `≥ 120 Hz percebidos` como alvos genéricos | ⚠️ Fisicamente impossíveis em monitor 60 Hz (vsync ≈ 16,7 ms). Reescrever metas **condicionadas à taxa de refresh** + metodologia de medição em 5 fases |
| R7 | Camadas `Core / Rendering / Platform.Windows / App / Shell` | ⚠️ `Rendering` e `App` estão mal delimitados (renderer WPF moraria no lugar errado; `App` depende de concreto). Fundir/simplificar para **3 projetos + 1 de export lazy** |
| R8 | MVP com 10 itens incluindo linha+seta+5 hotkeys globais+zoom+pan | ⚠️ Grande demais para validar o loop crítico. Reduzir para **MUST enxuto** (abrir→desenhar→apagar→desfazer→esconder→interagir) |

**Nenhum BLOCK impeditivo de spikes.** Há 2 vetos de implementação (R1/render único, R4/hotkeys) que impedem *codar o MVP*, mas não impedem *rodar os spikes* — desde que os spikes sejam reescritos com critérios de aceite objetivos (seção 13).

---

## 2. Decisions Confirmed (o que o revisor confirma sem ressalvas)

1. **Nativo > Web no caminho crítico (ADR-01).** Correto. Chromium no loop de tinta troca o diferencial (latência/idle/RAM) por conveniência. Tabela comparativa do doc original é justa.
2. **WPF como default Windows (ADR-02, com ressalva de escopo na §3).** Para time C#, WPF tem o menor risco de overlay + input pointer + packaging imediato. Qt continua sendo o plano B legítimo se portabilidade < 12 meses; Avalonia é rota de fuga, não ponto de partida (menos evidência de overlay/click-through).
3. **Overlay puro, sem captura (ADR-03).** Correto e importante: captura congela o app abaixo e muda a promessa do produto. Fullscreen exclusivo documentado como limitação é a decisão honesta.
4. **N janelas (1 por monitor), não spanning (ADR-04).** Correto, especialmente com DPI misto e offsets negativos do desktop virtual.
5. **Undo por comandos (ADR-06), JSON sem DB (ADR-08), sem telemetria com rede (ADR-10), portátil + Inno (ADR-09).** Todas confirmadas.
6. **Lápis = preset no MVP (ADR-05).** Confirmada e deve ser estendida: nem textura de ruído no MUST (ver §11).
7. **Toolbar flutuante + tray + hotkeys.** Melhor custo/benefício que radial/fixa. Confirmed.

---

## 3. Decisions That Need Revision

### 3.1 WPF vs Avalonia vs Qt — o documento subestima 2 custos
- **Acerto:** WPF vence no risco imediato Windows.
- **Premissa frágil 1:** "migração WPF→Avalonia = 20–30% (só Shell)". Otimista. XAML WPF ≠ XAML Avalonia ( Attached properties, `InkCanvas` inexistente, `HwndHost`/interop diferentes, threading do render Skia vs DirectX, packaging). Se o Core for realmente puro (sem `System.Windows`, sem tipos WPF no modelo) o número é plausível para *lógica*; para *janelas/overlay/input* é mais perto de reescrever a Shell inteira. **Correção:** vender a portabilidade como "Core + Document + Undo + Export preservados; Shell reescrita", não como percentual baixo. Isso não muda a escolha, muda a expectativa.
- **Premissa frágil 2:** Qt avaliado só por "custo C++ / contratação". Falta o ponto técnico real: Qt entrega o *mesmo* overlay com *menos* P/Invoke manual (QWindow flags + `Qt::WindowTransparentForInput` + `WA_TranslucentBackground` são de primeira classe e cross-platform). Se o produto virar multi-plataforma de verdade, Qt tem menos "cola Win32" para reescrever que WPF→Avalonia. **Correção:** registrar que WPF minimiza risco *Windows-MVP*, Qt minimiza risco *multi-OS futuro*. A escolha WPF continua válida porque o pedido diz "Windows primeiro", mas o trade-off deve estar explícito.
- **Avalonia agora?** Não. Menos exemplos de `WS_EX_TRANSPARENT` + Topmost + PerMonitorV2 + fullscreen multi-monitor que WPF. Entrar com Avalonia no MVP troca risco conhecido (WPF maduro) por risco desconhecido exatamente no requisito mais sensível (overlay). **Nova decisão:** WPF no MVP; Avalonia só como spike condicional se P1/P3 falharem.

### 3.2 Modelo de strokes / RDP / Undo — 3 ajustes
- **Confirmado:** `Stroke{Id, Tool, Color, WidthDip, Opacity, Points, Bounds}` + `Document{Strokes, Camera}` + comandos Add/Erase/Clear. Bom desenho.
- **Revisão 1 — `Guid` por stroke + `float ZoomApplied?` + `Ticks` por ponto:** `Guid` (16 bytes) para milhares de strokes é desperdício; `int` sequencial basta e é mais rápido para hit-test/mapa. `Ticks` por ponto só faz sentido se for medir velocidade/pressão futura — no MVP é peso morto que dobra o tamanho do ponto. **Nova decisão:** `Pt{X,Y}` (2 floats) + `Pressure` opcional só em build futura; `Id:int`; remover campos especulativos do MUST.
- **Revisão 2 — RDP em tempo real:** o doc sugere "RDP moderado" na captura. RDP clássico é O(n²) ou O(n log n) e roda sobre a polilinha *completa* — aplicá-lo a cada `PointerMove` é caro e errado. O correto é **filtragem incremental na captura (distância mínima + ângulo / OneEuro leve) e RDP (ou Douglas-Peucker / radial-distance) só no `PointerUp` (commit)**. **Nova decisão:** captura = `if dist > epsilon(QDpi)` anexa; commit = simplifica uma vez; `epsilon ≈ 0.75–1.5 DIP` tunado em spike, não chutado.
- **Revisão 3 — Undo "≥ 50 / 100 passos":** número arbitrário sem orçamento de memória. Comandar por contagem sem limite de pontos permite 100 strokes gigantes estourarem RAM. **Nova decisão:** limite duplo — max 50 comandos **e** teto de pontos em undo (ex.: 250k pontos; ao exceder, descarta o mais antigo / compacta Clear). Medido em P6.

### 3.3 Persistência / Export / Segurança —超半 confirmações com 2 correções
- **Settings JSON + `.bak` + debounced:** confirmado. Falta explicitar: escrita atômica (tmp + rename), schema version (`"v":1`), e que settings **nunca** bloqueiam startup (load assíncrono com defaults).
- **Export PNG "MVP tardio, custo baixo":** parcialmente otimista. PNG da tinta exige: (a) rasterizar `Document` com o *mesmo* raster do interativo (senão export ≠ tela — bug clássico de marker/opacity/linecap), (b) decidir fundo transparente vs. composited (sem captura, só transparente faz sentido), (c) resolver escala (1x DIP vs. 2x para 4K). Com Skia offscreen isso é factível, mas **só é "custo baixo" se o renderer de export compartilhar a especificação de traço com o renderer interativo**. Daí a exigência da §7 (uma spec de stroke, dois backends). **Nova decisão:** PNG = SHOULD (não MUST), com aceite "pixel-paridade ±1 LSB em casos de teste vetoriais".
- **Segurança/distribuição:** Inno + assinatura confirmados. Adições obrigatórias: (a) SmartScreen exige certificado EV **ou** reputação acumulada — orçar tempo/dinheiro, não é "assinar e pronto"; (b) UAC/e elevação: app **não** deve pedir admin (se pedir, quebra a promessa "leve"); documentar que overlay não cobre app elevado por UIPI (ver §8); (c) single-instance via Mutex + `--panic` (ver §10); (d) update v1 = verificação manual, sem auto-update silencioso.

---

## 4. Technical Risks Found (além dos R-01…R-10 originais)

| ID | Risco novo / subestimado | Por que o doc original é otimista | Severidade |
|----|--------------------------|-----------------------------------|------------|
| N-01 | **UIPI: overlay não elevado nunca fica acima de app elevado** (VS admin, Task Manager elevado, instaladores). `SetWindowPos(TOPMOST)` falha silenciosamente por integridade | Doc cita "app elevado" de passagem, sem explicar UIPI nem solução (elevar o app inteiro é inaceitável p/ utilitário) | Alta |
| N-02 | **Ativação/foco: overlay clicável rouba foco do app abaixo** a cada `Down`, quebrando digitação/apresentação | Doc assume `SW_SHOWNOACTIVATE` resolve; na prática cada janela clicável ativa a menos que `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE=MA_NOACTIVATE` + toolbar separada. Requer spike | Alta |
| N-03 | **WPF airspace + layered: `AllowsTransparency=true` força render por software em alguns caminhos** (janela layered WPF perde aceleração HW em cenários clássicos) | Doc fala "DirectX por baixo" sem ressalva. `WindowStyle=None + AllowsTransparency=True` em WPF historicamente cai para software ou tem custo alto em fullscreen 4K | Alta |
| N-04 | **"Dirty rect pelo app" não existe em WPF retained** | Doc promete repaint parcial controlado. WPF/DWM compõem a cena; o ganho real é não reconstruir geometria, não recortar pixels manualmente | Média-Alta |
| N-05 | **Coordenadas virtuais + mergulho negativo + DPI misto quebram hit-test ingênuo** | Doc propõe "documento virtual único" sem detalhar offset por monitor, arredondamento DIP→px e tolerância de borracha escalada | Média-Alta |
| N-06 | **Stylus/Pointer coalesced no WPF exige `StylusPlugIn`, não `MouseMove`** | `MouseMove` entrega ~60–125 Hz com jitter; `WM_POINTER` + `GetCoalescedEvents` / `StylusPlugIn.OnStylusMove` entrega o caminho de baixa latência. Doc cita mas não torna obrigatório | Média-Alta |
| N-07 | **120/144 Hz só existem se a cadeia inteira for 120 Hz+** (mouse 125 Hz + DWM 60 Hz = 60 Hz percebidos) | Metas citam 120 Hz sem exigir hardware de teste nem PresentMon | Média |
| N-08 | **Screen-share (Teams/Meet/Zoom) pode não capturar o overlay** (captura por janela vs. composição DXGI) | Doc pergunta "tinta aparece?" sem resposta. Para aulas remotas isso é crítico: se o overlay for `WS_EX_LAYERED`/`TOPMOST` fora da árvore capturada, o aluno não vê a tinta | Alta p/ persona educação |
| N-09 | **Hook global (`WH_KEYBOARD_LL`) como "fase 2" é armadilha de AV/privacidade** | Doc sugere evoluir para hook low-level. Hook global aumenta falsos-positivos de Defender, latência de teclado sistêmica e risco de travar input se o app congelar. `RegisterHotKey` deve ser o teto, não o piso | Média-Alta |
| N-10 | **RDP mal aplicado destrói curvas rápidas / marker largo** | Tolerância única para pen fina e marker 20 px gera artefatos diferentes. Requer tolerância proporcional à largura | Média |

---

## 5. Recommended Changes (tabela de mudança formal)

| Decisão | Original | Problema | Nova decisão | Motivo / Impacto / Custo |
|---------|----------|----------|--------------|--------------------------|
| Render interativo | InkCanvas *ou* DrawingVisual + Skia | Incoerente; dois modelos de stroke; InkCanvas não existe em Avalonia; Skia no loop WPF = cópia CPU | **Só DrawingVisual (1 visual por stroke, active reconstruído, finalizados `Freeze`)**; InkCanvas proibido no caminho crítico; Skia só export | Coerência + portabilidade da spec; impacto: reescrever §9; custo: baixo se decidido agora, alto se após Fase 3 |
| Dirty regions | App controla dirty rect | WPF não expõe isso | **"Minimizar reconstrução" (só active) + `Clip=Bonds inflado` + sem loop contínuo**; medir com PresentMon, não prometer recorte de pixels | Expectativa honesta; custo zero |
| Zoom/Pan | No MVP (Ctrl+Wheel, Space) | Multiplica coordenadas/input/hit-test/multi-mon por valor baixo | **Fora do MVP; `Camera=identidade travada`; struct permanece p/ futuro** | -30% complexidade overlay/input; custo: remover da toolbar/atalhos agora |
| Hotkeys globais | Ctrl+Z global, Alt+D, F9/F10, 4–5 globais | Sequestra undo alheio; colide com browser/IDE | **Só 3 globais de duplo modificador + pânico; todo o resto local** (ver §10) | Segurança UX; custo: remapear + documentar |
| Watchdog z-order | Polling 500 ms | Quebra idle ~0% | **Event-driven (`WinEventHook EVENT_SYSTEM_FOREGROUND` + `DwmCompositionChanged` + `DisplayChange`) + reassert sob demanda + botão/tray "trazer tinta p/ frente"** | Idle real ~0%; custo: +1 spike de validação |
| WPF layered | `AllowsTransparency=true` implícito | Risco software-fallback (N-03) | **Spike decide entre (a) WPF `AllowsTransparency` vs (b) `HwndHost`/WinForms+Direct2D vs (c) janela Win32 pura com DirectComposition; MVP usa o vencedor, não o presumido** | Evita retrabalho de render inteiro; custo: 2 dias de spike |
| RDP | Na captura, tol. 0.75 DIP | Custo por move + artefato em marker | **Filtro incremental na captura + RDP uma vez no commit, tolerância ∝ largura** | Latência menor; custo: teste unitário dedicado |
| Undo limite | 50–100 passos | Sem teto de memória | **50 comandos + teto de pontos (250k) + Clear compactado** | RAM previsível; custo zero |
| Arquitetura | 5 projetos | `Rendering`/`App` mal delimitados | **3 projetos + export lazy** (ver §6) | Menos fricção; custo: renomear antes de criar |
| Export PNG | "MVP tardio barato" | Paridade pixel não garantida | **SHOULD com aceite de paridade; fundo transparente; escala 1x/2x explícita** | Evita bug "export ≠ tela"; custo: suite de paridade |

---

## 6. Revised Architecture (simplificada, sem camadas por dogma)

```
src/
  EpicPencil.Core/            # PURO .NET (sem System.Windows, sem DllImport):
                              #   Stroke/Document/Camera(travada)/Undo(Ring+cap)/Rdp/
                              #   HitTest(bounds+segmento)/SettingsSchema(v1)/
                              #   StrokeSpec (a ÚNICA spec de traço: caps, joins, composição marker)
                              #   ExportJob (definição, não implementação Skia)
  EpicPencil.Windows/         # TUDO Win32 + SO: OverlayCtrl, Hotkeys(RegisterHotKey),
                              #   Monitors/Dpi, SettingsStore(arquivo atômico),
                              #   SingleInstance(Mutex+pipe+--panic), ScreenShareNote
  EpicPencil.Shell/           # WPF APENAS: OverlayWindow xN, Toolbar, Tray,
                              #   WpfStrokeRenderer (DrawingVisual; lê StrokeSpec),
                              #   InputForwarder (Pointer→Core), AppState (modo/ferramenta)
  EpicPencil.Export.Skia/     # LAZY-LOADED (só quando exportar): SkiaRenderer (lê a MESMA StrokeSpec)
tests/EpicPencil.Core.Tests/
```

**Regras de dependência (inversão explícita):**
- `Core` não referencia ninguém. Interfaces de SO (`IHotkeyService`, `IMonitorService`, `ISettingsStore`, `IClock`) são **declaradas no Core/App e implementadas em `Windows`** — nunca o contrário (o doc original deixava `App` depender de concreto).
- `Shell` referencia `Core` + `Windows` via interfaces. Nenhum `DllImport` fora de `Windows`; nenhum `System.Windows` fora de `Shell` (+ `Export.Skia` referencia só `Core` + SkiaSharp).
- `Export.Skia` **não carrega no startup** (`Assembly.Load` sob demanda) — protege RNF-01/02.
- Sem DI container, sem MVVM framework, sem event bus no MVP (confirmado do doc original). `AppState` = 1 classe observável com `INotifyPropertyChanged` manual.

**O que saiu:** projeto `Rendering` genérico (vira `StrokeSpec` dentro do Core + 2 backends finos) e projeto `App` separado (vira 1 pasta `state/` dentro da Shell — separar `App`/`Shell` antes de ter 2 frontends é overengineering).

---

## 7. Revised Rendering Strategy (um caminho principal)

**Veredito técnico comparativo:**

| Candidato | Latência | Controle (marker/linha/seta/borracha) | Custo MVP | Portabilidade da spec | Veredito |
|-----------|----------|----------------------------------------|-----------|------------------------|----------|
| **InkCanvas** | Boa (Stylus nativo) | Baixo (composição marker errada por overlap; linha/seta exigem gambiarra; eraser semantics próprias) | Baixo inicial, alto depois | Péssimo (só WPF; não existe em Avalonia) | ❌ Fora do caminho crítico |
| **DrawingVisual + DrawingContext (WPF/DirectX retained)** | Ótima (reconstrói só 1 visual; congelados vão p/ GPU) | Total (StreamGeometry, Pen caps/joins, DrawingGroup p/ marker, Geometry hit-test) | Médio (mais código que InkCanvas, menos que D2D) | Bom (spec pura no Core; backend fino) | ✅ **Principal MVP** |
| **SkiaSharp interativo dentro do WPF** | Média (raster CPU + upload WriteableBitmap por frame; GPU backend exige interop GRContext/DXGI manual) | Total | Médio-Alto | Ótimo | ❌ Como loop interativo; ✅ só export offscreen |
| **Direct2D puro (Vortice/SharpDX + HWND)** | Máxima | Total | Alto (device lost, resize, DPI, multi-HWND na mão) | Bom | ❌ MVP (vira opção se spike provar fallback de software no WPF — ver N-03) |

**Estratégia revisada (obrigatória):**
- **Stroke ativo:** 1 `DrawingVisual` reconstruído a cada lote coalescido de pointer (não a cada evento bruto). Geometria = `StreamGeometry` (nunca `Polyline` com Points mutável — aloca). `Pen{Brush, Thickness, StartLineCap=Round, EndLineCap=Round, LineJoin=Round}`. Congelar `Pen/Brush` (`Freeze`) uma vez por ferramenta.
- **Strokes finalizados:** 1 `DrawingVisual` cada, `Freeze()` após commit. Container = 1 `ContainerVisual` por overlay (ordem = z-order). Remoção/undo = `Children.Remove` (O(1) lógico; sem rebuild da cena).
- **"Dirty regions":** renomear para **reconstrução mínima**. WPF compõe; o app garante: (a) só o active é reaberto; (b) `Clip` do active = `boundsAtivo inflado por (width/2 + 2px)`; (c) zero `InvalidateVisual` full; (d) `CompositionTarget.Rendering` assinado só durante gesto. Medir com PresentMon/ETW, não afirmar recorte manual.
- **Marca-texto:** `DrawingGroup` com 1 geometria do stroke inteiro + `Opacity 0.35–0.45` composta **uma vez** (nunca carimbar segmentos alfa sobrepostos — isso escurece o overlap, o bug clássico). Cor default amarelo com `Opacity` fixa; sem `Multiply` no MVP (composição do DWM sobre conteúdo alheio já é variável).
- **Borracha (por stroke):** hit-test = `boundsInflado` → `geometry.StrokeContains(penHit, ponto)` (tolerância = `max(6 DIP, width/2)`). Apaga todos intersectados pelo segmento do cursor (não só o ponto) para gesto rápido não "pular" strokes.
- **Linha/seta:** mesmo pipeline: `Down` guarda âncora → `Move` reconstrói preview (linha ou linha + cabeça poligonal proporcional à largura) → `Up` commita como stroke de 2–4 pontos (seta guarda `tip+angle` derivado, não entidade nova — evita tipo especial no undo).
- **Export PNG:** backend Skia offscreen que implementa **a mesma `StrokeSpec`** (round caps/joins, opacity, ordem). Aceite: paridade visual em suite de 20 casos (linhas, curvas, marker sobreposto, setas, 1x/2x).

---

## 8. Revised Overlay Strategy (Win32 sem ingenuidade)

**Análise das primitivas (não aceite `WS_EX_TRANSPARENT` como mágica):**

| Primitiva | O que realmente faz | Quando usar |
|-----------|---------------------|-------------|
| `WS_EX_LAYERED` | Habilita superfície com alfa por pixel + composição DWM. Pré-requisito da transparência real | Sempre nas overlays |
| `WS_EX_TRANSPARENT` | **Hit-test: mouse atravessa** a janela para a de baixo. Não afeta visibilidade; exige `SetWindowPos(SWP_FRAMECHANGED)` após toggle para valer | Toggle desenho↔interagir (com janela separada da toolbar) |
| `WM_NCHITTEST → HTTRANSPARENT` | Hit-test **por região/ponto** (granular). Permite "janela clicável só no pixel de tinta" sem trocar ex-style | Alternativa se um dia fundir toolbar+overlay; no MVP com janelas separadas, o toggle de ex-style é mais simples e auditável. Spike deve medir os dois, mas o default é toggle |
| `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE → MA_NOACTIVATE` | Impede a overlay de **roubar foco/ativação** ao clicar/desenhar | Obrigatório sempre (N-02). Sem isso cada traço tira foco do Word/jogo |
| `SetWindowLongPtr(GWL_EXSTYLE)` | Troca o estilo em runtime | Só no toggle de modo; nunca no `Move` (custo + flicker) |
| `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE\|NOREDRAW...)` | Reasserção de z-order **sem ativar** | Só sob evento (foreground mudou, DWM mudou, display mudou), nunca polling cego |
| `ShowWindow(SW_SHOWNOACTIVATE)` | Exibe sem ativar | Criação/restauração das overlays |

**Estratégia revisada:**
1. Overlay = `WS_POPUP | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `ShowInTaskbar=false`, 1 por monitor em `rcMonitor` exato.
2. **Modo desenho:** sem `TRANSPARENT`; `WM_MOUSEACTIVATE=MA_NOACTIVATE` (desenha sem ativar); cursor anel.
3. **Modo interagir:** com `TRANSPARENT` (cliques atravessam de verdade); tinta visível; overlay jamais focável.
4. **Z-order sem polling:** `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + `WM_DISPLAYCHANGE`/`DwmCompositionChanged` → reassert com `NOACTIVATE`. Proibido timer 500 ms no MVP (quebra idle). Botão "Trazer tinta à frente" no tray como fallback manual.
5. **UIPI/e elevação (N-01):** documentar como limitação — app não-elevado não cobre app elevado. Não elevar o app. Spike P1 deve evidenciar (screenshot VS admin por cima da tinta) para o texto de release notes nascer de evidência.
6. **Software-fallback (N-03):** spike P1 mede FPS/CPU em 4K com `AllowsTransparency=True`. Se cair para software ou CPU > teto, o MVP migra a Shell para **WinForms/Direct2D ou HWND puro** mantendo o Core intacto — por isso o Core não pode vazar WPF.

---

## 9. Revised Input Strategy

- **Fonte primária: `WM_POINTER` / `StylusPlugIn`, não `MouseMove`.** `MouseMove` (≈64–125 Hz, sem coalescing) impõe teto de fluidez e jitter. Exigir `GetPointerInfo + GetCoalescedEvents` (ou `StylusPlugIn.OnStylusMove` com `RawStylusInput`) e filtrar por `dist > epsilon`.
- **Botões:** esq = desenhar/apagar (conforme ferramenta); meio ou `Espaço+arrasto` = *reservado futuro* (sem pan no MVP, meio pode alternar modo — configurável depois); direito = **nunca** desenha; no overlay escuta só para cancelar preview (`Down direito` cancela linha/seta), nunca menu global.
- **Wheel:** `Ctrl+Wheel` **fora do MVP** (sem zoom). Wheel puro sobre overlay em modo desenho = ignorado (não rouba scroll do app abaixo; em modo interagir o wheel já atravessa naturalmente).
- **Teclado local:** `PreviewKeyDown` na toolbar/overlay quando focados. **Nenhum hook global** (`WH_KEYBOARD_LL` proibido no MVP — ver N-09).
- **Toque/tablet reais:** fora do MVP; modelo já carrega `Pressure` mas input ignora. Não prometer pressão na v1.
- **Single-instance + foco:** Mutex + pipe/named-pipe + `--panic` (ver §10).

---

## 10. Revised Hotkey Strategy (segurança primeiro)

**Princípio:** o app é hóspede do sistema. Em modo interagir ele é invisível ao input; em modo desenho ele é dono temporário. **Nenhum atalho de 1 tecla ou `Ctrl+letra` pode ser global.**

| Ação | Tipo MVP | Default proposto | Justificativa |
|------|----------|------------------|---------------|
| **PANIC: sair de desenho + click-through + mostrar toolbar** | 🌍 Global | `Ctrl+Alt+Shift+X` ( + `--panic` via 2ª execução + botão tray) | Duplo+ modificador raríssimo; memorizável como "X de emergência". Sempre funciona, mesmo se overlay travada em desenho |
| Alternar desenho/interagir | 🌍 Global | `Alt+Shift+D` | `Alt+D` puro colide (browsers: focar address bar). Duplo modificador evita sequestro |
| Esconder/mostrar tinta | 🌍 Global | `Alt+Shift+H` | `F9/F10` colidem com debuggers/IDEs; Alt+Shift é seguro |
| Tudo o resto (P/B/E/H/L/S, Ctrl+Z/Y, Del, 1/2/3, Esc) | 🖥️ Local apenas | Quando overlay/toolbar focados | `Ctrl+Z` global é **veto permanente**: sequestraria undo do Word/VSCode/browser. `C/Del` globais idem |
| Sair do modo desenho | Local | `Esc` (nunca apaga) | Estado seguro sempre a 1 tecla |

**Regras:** máximo 3 globais no MVP (2 funcionais + pânico); todos `RegisterHotKey` (nunca hook); todos com ≥2 modificadores ou F-key alta dedicada; remapeamento total é POST-MVP (só documentar os defaults + conflitos testados em Word/VSCode/Chrome/PowerPoint/Teams).

---

## 11. Revised MVP (classificação MUST/SHOULD/NICE/POST)

**Loop de validação (o MVP só existe para provar isto):**
> abrir → desenhar → apagar → desfazer → esconder → interagir → desenhar novamente — com latência baixa e sem prender o usuário.

| Feature | Classe | Nota do revisor |
|---------|--------|-----------------|
| Overlay fullscreen/monitor + Topmost + click-through toggle | **MUST** | Coração; sem isso nada mais importa |
| Caneta | **MUST** | |
| Borracha por stroke | **MUST** | Mais importante que marker p/ validar loop |
| Undo / Redo (local) | **MUST** | Sem undo, Clear é destrutivo e beta trava |
| Toolbar flutuante + tray + indicador de modo + cursor anel | **MUST** | Sem indicador o usuário se perde |
| Toggle desenho/interagir + esconder tinta + PANIC global | **MUST** | Segurança > features (seção H) |
| 6 cores preset + S/M/L por ferramenta + settings JSON + single-instance | **MUST** | Sem settings o beta não retém preferência |
| Multi-monitor + PerMonitorV2 | **MUST** | Sem isso é brinquedo de 1 tela; mas matriz de teste reduzida ( §13) |
| Marca-texto (1 composição correta) | **SHOULD** | Alto valor aula; só entra se paridade marker validada em P2, senão escorrega p/ 1.1 |
| Linha reta (Shift ou `L`) | **SHOULD** | Mesmo motor da seta; preview elástico obrigatório |
| Seta | **SHOULD** | Se linha entrar, seta custa +20% — manter juntas ou cortar as duas |
| Export PNG (transparente, 1x/2x) | **SHOULD** | Condicionado à paridade (§7); se falhar, sai sem culpa |
| Lápis com textura, slider opacidade, picker custom completo | **NICE** | Preset resolve; não gastam spike |
| Zoom / Pan / `Ctrl+Wheel` / `Espaço+arrasto` | **POST-MVP** | Removidos (ver §5/R3). `Camera` travada |
| Borracha por área, retângulo/elipse, seleção/mover, sessão save, SVG, remapeamento UI, auto-update, tablet pressão, modo captura | **POST-MVP** | Confirmado fora |

**Corte líquido vs. doc original:** saem zoom, pan, 2–3 hotkeys globais extras, promessa de "120 Hz" incondicional e `Rendering` genérico; entram pânico global e critérios de paridade PNG. MVP fica ~30% menor e 100% focado no loop crítico.

---

## 12. Revised Performance Targets (honestos, condicionados, mensuráveis)

### 12.1 Correções às metas originais
- **Startup `< 500 ms`:** RAZOÁVEL como *warm start* (2ª abertura, .NET já em cache) em SSD/i5. Como *cold boot* (pós-reboot, Defender scan) é AGRESSIVO para WPF sem NativeAOT. **Revisada:** warm ≤ 500 ms (MUST), cold ≤ 900 ms (SHOULD). Medir do duplo-clique ao primeiro `WM_PAINT` da toolbar, não ao `Main()` (auto-engano comum).
- **RAM `< 60/80 MB` idle:** RAZOÁVEL (WPF base ~30–50 MB). `< 35 MB` é AGRESSIVO e exige trimming + `Export.Skia` lazy + zero WebView + 1ª janela sob demanda. **Revisada:** MUST ≤ 80 MB privado após 5 min idle; SHOULD ≤ 60 MB; STRETCH ≤ 40 MB. Medir *Private Working Set*, não "Memória" do Task Manager (que inclui compartilhada).
- **CPU idle `< 1% / ~0%`:** RAZOÁVEL **se e somente se** sem polling, sem animação, sem blur, sem `CompositionTarget` assinado fora de gesto. Com watchdog 500 ms vira ~0,5–2% e falha. **Revisada:** MUST < 1% média 5 min (ETW), com a arquitetura event-driven da §8.
- **Latência `< 16 ms / alvo < 8 ms`:** META MAL FORMULADA. Em monitor 60 Hz, só o vsync+DWM já consomem até 16,7 ms de *presentation* — prometer `< 8 ms end-to-end` em 60 Hz é **fisicamente impossível**. **Revisadas (condicionadas):**
  - MUST (60 Hz): p50 end-to-end ≤ 25 ms, p95 ≤ 40 ms.
  - MUST (120/144 Hz): p50 ≤ 16 ms.
  - STRETCH (120 Hz+ + pointer prediction): p50 ≤ 10 ms. Nenhum "alvo < 8 ms" sem hardware 144 Hz+ e medição por câmera.
- **"120 Hz percebidos":** AGRESSIVO sem mouse ≥ 500 Hz + tela 120 Hz + PresentMon. Vira SHOULD condicionado ao hardware de teste.
- **4K:** custo é ~4× pixels no compose + geometria mais longa. Meta separada: rabisco 4K não pode exceder 2× o CPU de 1080p (se exceder, há rebuild full — bug).

### 12.2 As 5 latências (separar para não trapacear)
1. **Input latency:** hardware+driver+fila OS (mouse 125 Hz = até 8 ms só aqui; 1000 Hz = ~1 ms). Fora do controle do app — registrar o hardware do teste.
2. **Processing latency:** `Pointer→coalesce→filtro→append→rebuild active visual`. Alvo MUST < 3 ms p50 (medido por timestamps internos `QueryPerformanceCounter`).
3. **Render latency:** `rebuild→WPF compose→GPU`. Alvo MUST < 6 ms p50 (ETW `Microsoft-Windows-WPF` + GPU time).
4. **Presentation latency:** `Present→DWM vsync→fóton`. Em 60 Hz: 0–16,7 ms (fora do controle; só se reduz com refresh maior, não com código).
5. **Perceptual end-to-end:** soma 1–4 como o usuário sente. É a única que importa para "parece instantâneo", mas é a soma — otimizar só o código (2–3) sem medir 1+4 é auto-engano.

### 12.3 Metodologia realista (sem laboratório)
- **Hardware de referência travado:** i5 11ª+/8 GB/SSD + mouse comum 125 Hz + monitor 60 Hz (base) **e** 1 máquina 120 Hz+ para o teste STRETCH. Toda medição cita o hardware.
- **Ferramentas:** `QueryPerformanceCounter` interno (fases 2–3) + **PresentMon** (fases 3–4) + ETW/WPA para CPU/idle + script de rabisco sintético (círculos a velocidade constante — repetível, ao contrário de "rabisco manual"). Sem câmera high-speed no MVP (documentar como limitação; percepção validada por teste cego A/B com 5 usuários beta).
- **Oráculos:** falha se warm startup > 500 ms, idle > 1%, p50 end-to-end 60 Hz > 25 ms, ou regressão > 15% no CI perf.

---

## 13. Required Spikes (reescritos com aceite objetivo — cada um ≤ 2 dias)

| Spike | Pergunta que mata o projeto se "não" | Aceite GO (mensurável) |
|-------|--------------------------------------|------------------------|
| **S1 — Fantasma + UIPI + screen-share** (era P1) | A tinta fica visível e no topo sobre browser/YouTube/VS **não-elevado**, e o comportamento sobre app **elevado** e em **Teams/Meet share** é caracterizado? | Screenshot-evidence: (a) Topmost OK não-elevado; (b) sobre app elevado documentado como limitação UIPI; (c) share testado: tinta visível SIM/NÃO por ferramenta (vara de release notes). + Medida `AllowsTransparency` 4K: CPU/GPU abaixo do teto ou decisão de trocar Shell |
| **S2 — Traço vivo** (era P2, agora DrawingVisual obrigatório) | Stroke ativo com pointer coalescido reconstrói só 1 visual com p50 proc < 3 ms e end-to-end dentro da §12? | Rabisco sintético 30 s: processing p50 < 3 ms; PresentMon end-to-end dentro das metas 60 Hz; marker sem double-darkening (teste visual + paridade) |
| **S3 — Click-through + foco** (era P3, expandido) | Toggle `TRANSPARENT` < 50 ms **sem roubar foco** em 100 alternâncias? | 100 toggles: tempo p95 < 50 ms; foco do app abaixo preservado (audit `GetForegroundWindow` antes/depois); clique atravessa e ativa o app abaixo; `Esc`/pânico sempre devolvem controle |
| **S4 — Multi-mon + DPI** (era P4, matriz reduzida) | 2 monitores com DPI misto posicionam tinta no pixel certo após plug/unplug/sleep? | Matriz mínima: 100+100, 100+150, 4K+1080p; régua de calibração erro < 1 DIP; sobrevive a unplug/replug + troca de primário sem perder strokes |
| **S5 — Hotkeys sem sequestro** (era P5, escopo travado) | Os 3 globais funcionam com Word/VSCode/Chrome/PowerPoint focados **sem quebrar nenhum atalho deles**? | Checklist de colisão executado nas 5 apps (incl. `Ctrl+Z` local deles intacto); globais só disparam os 3; teste de stress: 50 disparos sem foco perdido |
| **S6 — Soak + undo + export-paridade** (era P6, fundido) | 10k strokes + Clear/Undo + PNG export mantêm RAM/handles/latência? | 10k strokes: commit p95 < 16 ms; undo/clear < 100 ms; RAM volta a ±10% após Clear+Undo; handles estáveis; PNG 1x/2x com paridade ±1 LSB nos 20 casos |

Ordem: S1→S2→S3 (núcleo; se qualquer um falhar, parar e re-decidir Shell) → S4→S5→S6. **Só S1–S3 verdes carimbam WPF.**

---

## 14. Final Go/No-Go Criteria

### GO (maduro para implementar o MVP revisado — sem spike adicional)
- Stack .NET + WPF + Core puro + overlay puro + N janelas + undo por comandos + JSON + Inno + offline-first.
- Toolbar flutuante + tray + modos desenho/interagir + indicador + cursor anel.
- Câmera travada (sem zoom/pan), `Pt{X,Y}`, IDs inteiros, filtro incremental + RDP no commit.
- Arquitetura de 3 projetos + export lazy (§6) e renderer único DrawingVisual (§7).
- Hotkeys locais livres + só 3 globais de duplo modificador + pânico (§10).

### SPIKE (exige S1–S6 verdes antes de codar o MVP)
- Toda a cadeia overlay: Topmost real, UIPI caracterizado, screen-share caracterizado, `AllowsTransparency` validado em 4K (S1).
- Latência decomposta em 5 fases dentro das metas condicionadas (S2).
- Toggle click-through < 50 ms sem roubo de foco (S3).
- DPI misto + plug/unplug sem deriva (S4).
- 3 globais sem colisão nas 5 apps de referência (S5).
- Soak 10k + paridade PNG (S6).
- Se S1 provar software-fallback: SPIKE adicional obrigatório de Shell alternativa (WinForms/D2D) antes da Fase 1.

### BLOCK (impede início da implementação — resolver primeiro)
1. **BLOCK-1 — Nenhum:** não há impeditivo para *iniciar os spikes*. ✅ Spikes liberados.
2. **BLOCK-2 — Implementação do MVP bloqueada até:** (a) renderer único DrawingVisual adotado e InkCanvas removido do caminho crítico; (b) hotkeys reescritas (veto `Ctrl+Z` global); (c) zoom/pan removidos do escopo; (d) watchdog por polling removido; (e) S1–S3 com aceite assinado. Sem esses 5 itens, codar o MVP gera retrabalho quase certo na engine de tinta e no instalador.

---

### Veredito final do revisor
O documento original acerta na direção e merece nota alta pela honestidade sobre fullscreen exclusivo e portabilidade. Mas ele promete **controle que o WPF não dá (dirty rects), portabilidade mais barata do que é, latência menor do que a física permite em 60 Hz e hotkeys que sequestram o usuário**. Com as 10 mudanças da §5 aplicadas e os spikes reescritos da §13 executados, o projeto fica **GO para spikes, NO-GO condicional para MVP** — exatamente onde um projeto deste risco deveria estar antes de escrever a primeira linha de produção.
