# Epic Pencil — Análise Arquitetural e Plano de Desenvolvimento
**Status:** Documento de arquitetura pré-implementação (NÃO implementar código de produção ainda)
**Data:** 2026-09-09 | **Autor:** Software Architect / Tech Lead | **Versão:** 1.0
**Objetivo:** Aplicativo desktop Windows, leve e rápido, para anotação/desenho sobre a tela (overlay), estilo Epic Pen, mas com UX e implementação próprias.

---

## 1. Visão geral do produto

**Proposta de valor:** camada de tinta digital sobre qualquer conteúdo do Windows (navegador, slides, IDE, vídeo, reunião) com fricção quase zero: abrir (< 500 ms), escolher ferramenta/cor/espessura (1–2 cliques ou 1 tecla), desenhar com latência imperceptível, apagar/desfazer, sair do caminho (click-through).

**Princípios-guia (ordem de prioridade):**
1. Performance e leveza (RAM/CPU/latência/startup).
2. Confiabilidade do overlay (sempre visível, nunca atrapalha, nunca perde tinta).
3. Simplicidade de UX (toolbar mínima, atalhos memorizáveis).
4. Portabilidade futura (Windows primeiro, sem aprisionar o core ao Win32).
5. Zero dependência de servidor; offline-first; privacidade total.

**Crítica a uma premissa do pedido:** "zoom/pan da tela" e "overlay ao vivo sem capturar a tela" são parcialmente conflitantes. Ampli­ar o *conteúdo abaixo* (ex.: dar zoom no navegador) sem capturá-lo é impossível para um overlay — o overlay só controla a própria camada de tinta. O documento resolve isso na §11 (zoom = transform da camada de tinta, não do desktop; captura/congelamento fica como modo futuro opcional).

**Anti-objetivos do MVP:** edição vetorial completa (seleção/mover nós), colaboração em rede, gravação de vídeo, OCR, conta/login, cloud sync.

---

## 2. Requisitos funcionais (RF)

### RF-01 Ferramentas de desenho (MVP)
| ID | Ferramenta | MVP? | Notas |
|----|------------|------|-------|
| RF-01.1 | Caneta | ✅ | Traço sólido, opacidade 100%, bordas arredondadas |
| RF-01.2 | Lápis | ✅* | Textura/granulado leve; se o custo for alto, MVP usa caneta com preset diferente (ver ADR-05) |
| RF-01.3 | Marca-texto | ✅ | Largura grande, opacidade ~30–50%, composição multiply/screen, fica "atrás" conceitualmente |
| RF-01.4 | Borracha (stroke) | ✅ | Apaga o stroke inteiro ao tocar (modo padrão — mais rápido e previsível) |
| RF-01.5 | Borracha por área/pixel | 🔶 Pós-MVP | Apagamento parcial exige geometria por segmento; custo alto, valor médio |
| RF-01.6 | Limpar tudo | ✅ | Com confirmação se > N strokes ou undo disponível |
| RF-01.7 | Linha reta | ✅ | Shift = linha reta temporária com qualquer ferramenta OU ferramenta dedicada; preview elástico |
| RF-01.8 | Seta | ✅ | Linha + cabeça (2 segmentos ou polígono); mesmo motor da linha |
| RF-01.9 | Formas (retângulo, elipse) | 🔶 Pós-MVP (retângulo/elipse primeiro) | Mesmo pipeline de preview elástico |
| RF-01.10 | Seleção/mover | ❌ MVP não | Funcionalidade mais cara (hit-test, handles, transform). Reavaliar só na fase vetorial |
| RF-01.11 | Undo / Redo | ✅ | Pilha de comandos, ≥ 50 passos, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z |

**Funcionalidades essenciais faltantes identificadas:**
- F-12: *Modo desenho vs. modo interagir* (toggle explícito + indicador visual) — sem isso o app é inutilizável.
- F-13: *Esconder/mostrar tinta* (sem apagar) e *esconder toolbar*.
- F-14: *Cursor/anel de cursor* que indica ferramenta+cor+espessura no modo desenho.
- F-15: *Conta de strokes / indicador de performance* interno (debug) — barato e evita regressões.
- F-16: *Pan da camada de tinta* (arrasto com espaço ou botão do meio) — necessário se houver zoom.
- F-17: *Exportar PNG* (deveria entrar no MVP tardio — apresentadores sempre pedem).

### RF-02 Controle do traço
Cor (paleta + custom), espessura (1–50 px lógicos, presets S/M/L), opacidade (fixa por ferramenta no MVP; slider só pós-MVP), presets por ferramenta (memoriza última cor/espessura de cada ferramenta), atalhos configuráveis (fase 2).

### RF-03 Overlay/tela
Fullscreen transparente por monitor, always-on-top, click-through alternável, multi-monitor (incl. adicionar/remover monitor em runtime), per-monitor DPI, coexistência com apps fullscreen e acelerados por GPU (best-effort — ver riscos).

### RF-04 Interface
Toolbar flutuante + tray icon + atalhos. Sem menus aninhados no MVP.

### RF-05 Atalhos
Locais (quando overlay focado) no MVP; globais (quando focado em outro app) no MVP apenas para: toggle desenho/interagir, undo, limpar, esconder. Globais completos pós-MVP.

---

## 3. Requisitos não funcionais (RNF)

| ID | Requisito | Meta MVP | Referência |
|----|-----------|----------|------------|
| RNF-01 | Startup (clique → pronto p/ desenhar) | < 500 ms (alvo < 300 ms) | Medido em i5/8 GB, SSD, após boot frio do app |
| RNF-02 | RAM em idle | < 60 MB (alvo < 35 MB) | Sem Chromium embutido |
| RNF-03 | CPU em idle | ~0% (< 1%) | Sem loop de render contínuo; só renderiza sob demanda + cursor |
| RNF-04 | CPU durante desenho rápido | < 10% (quad-core moderno) | 1 stroke ativo + repaints parciais |
| RNF-05 | Latência input→fóton | < 16 ms (alvo < 8 ms) | Mouse move → pixel visível; exige preview imediato + coalescing |
| RNF-06 | Fluidez | ≥ 120 Hz percebidos no stroke ativo em tela 120 Hz+ | Repaint parcial da dirty region |
| RNF-07 | Tamanho do instalador | < 15 MB (alvo < 10 MB) | Sem Electron |
| RNF-08 | Estabilidade overlay | Nunca perde z-order silenciosamente; recupera Topmost em 1 s | Watchdog de z-order + reassert |
| RNF-09 | DPI | Correto em 100/125/150/175/200%, misto por monitor | Per-Monitor V2 |
| RNF-10 | Offline | 100% funcional sem rede | Telemetria default OFF |
| RNF-11 | Acessibilidade mínima | Todos os comandos via teclado; contraste AA na toolbar | — |

---

## 4. Comparação das stacks

### 4.1 Electron (Chromium + Node)
- **Vantagens:** dev web rápido, canvas/SVG maduros, distribuição simples, cross-platform imediato.
- **Desvantagens (graves p/ este produto):** RAM idle típica 150–350 MB; startup 1–3 s; instalador 60–120 MB; loop do compositor Chromium adiciona 1–2 frames de latência (~20–40 ms); transparência click-through (`setIgnoreMouseEvents`) funciona mas tem histórico de bugs com multi-monitor/DPI/jogos fullscreen; consumo idle raramente chega a 0% (processos GPU/utility acordam).
- **Veredito:** ❌ Incompatível com RNF-01/02/05/07. Escolhê-lo seria trocar o diferencial do produto (leveza) por conveniência de desenvolvimento.

### 4.2 Tauri 2 (Rust + WebView2 nativo)
- **Vantagens:** binário pequeno (~5–10 MB), RAM bem menor que Electron (~40–80 MB), usa WebView2 do sistema, Rust no backend, updates embutidos.
- **Desvantagens:** o *frontend continua sendo WebView2/Chromium*: latência de tinta e tearing sujeitos ao compositor web; overlay transparente fullscreen + click-through + Topmost exigem interop Win32 manual fora do modelo Tauri (janelas decoradas, shadow, hit-test); API de janelas ainda menos madura que Win32 direto para layered windows; debugging de tinta sobre GPU alheia é mais difícil; complexidade híbrida (Rust + TS + Win32) sem o ganho de performance nativa no caminho crítico (o desenho roda no canvas web).
- **Veredito:** 🔶 Melhor que Electron, mas mantém o elo fraco (render web) exatamente no requisito mais sensível (latência de tinta). Não recomendado como stack principal.

### 4.3 .NET — WPF (.NET 8/9) / WinUI 3 / WinForms
- **WPF:** `InkCanvas` + stylus/pointer nativo foi literalmente feito para tinta de baixa latência; DirectX por baixo; interop Win32 total (layered, transparent, Topmost, hooks); startup ~200–400 ms; RAM ~25–45 MB; C# produtivo; ecossistema maduro.
  - Contra: Windows-only (WPF), tecnologia em modo manutenção (mas estável e suportada no .NET moderno); HiDPI exige manifesto + PerMonitorV2 (resolvido, mas exige teste).
- **WinUI 3 / Windows App SDK:** rendering moderno, mas empacotamento MSIX/ins­talação e ciclo de bugs ainda geram atrito para um utilitário que precisa ser "baixe e rode"; controle fino de overlay fullscreen multi-monitor é mais burocrático que Win32 puro.
- **Veredito:** ✅ Melhor risco/benefício para MVP Windows. WPF (ou WinForms + Direct2D/Skia se quiser controle total) entrega RNFs com menor risco técnico.

### 4.4 Qt 6 (C++/QML ou Widgets)
- **Vantagens:** cross-platform real (Win/Mac/Linux), `QPainter`/RHI com bom controle de GPU, janelas transparentes/click-through bem documentadas, instalador enxuto, RAM ~30–55 MB, sem vendor lock-in Microsoft.
- **Desvantagens:** C++ aumenta custo/tempo e barra de contratação no Brasil; QML é overkill para toolbar mínima; licenciamento LGPL exige link dinâmico + atenção (viável, mas é um item jurídico a gerenciar); pointer/ink tablet no Windows exige código dedicado de qualquer forma.
- **Veredito:** ✅ Segunda melhor opção; vira primeira se o time já domina C++/Qt OU se portabilidade Mac/Linux for exigida já no ano 1.

### 4.5 Outras alternativas avaliadas
| Alternativa | Avaliação |
|-------------|-----------|
| **Avalonia (.NET + Skia, XAML cross-platform)** | ⭐ Fortíssima: C# produtivo + Skia + Win/Mac/Linux. Transparência/Topmost OK; click-through via interop Win32 (factível, menos exemplos que WPF). RAM ~35–55 MB. Candidata natural se quiser portabilidade sem C++. Risco: menos exemplos de overlay que WPF/Qt → validar no Protótipo 1–3 |
| **Flutter Windows (Skia/Impeller)** | Bom render, mas janela transparente/click-through/fullscreen multi-monitor ainda depende de plugins imaturos + method channels; app mínimo ~40–70 MB; latência de tinta boa, integração Win32 fraca |
| **Rust puro (winit + wgpu/tiny-skia/vello + egui)** | Pegada mínima (~10–20 MB, binário < 5 MB), controle total; custo de dev altíssimo (tudo na mão: toolbar, DPI, hooks, packaging). Bom para engine, ruim para time-to-MVP |
| **Python (PyQt/wx)** | Prototipação rápida, distribuição/startup fracos, GIL e packaging afastam |
| **Wails / Neutralino** | Mesma crítica do Tauri (render web no caminho crítico) |
| **UWP** | Sandbox limita hooks/globais; descartado |

### 4.6 Tabela-resumo (pesos: performance e overlay têm peso 2)
| Critério | Electron | Tauri | WPF/.NET | Qt 6 | Avalonia |
|----------|:---:|:---:|:---:|:---:|:---:|
| RAM/startup (x2) | 1 | 3 | 5 | 4 | 4 |
| Latência tinta (x2) | 2 | 3 | 5 | 5 | 4 |
| Overlay Win32 (x2) | 2 | 3 | 5 | 4 | 3 |
| Multi-monitor/DPI | 2 | 3 | 5 | 5 | 4 |
| Produtividade dev | 5 | 4 | 5 | 3 | 4 |
| Portabilidade futura | 4 | 4 | 2 | 5 | 5 |
| Tamanho instalador | 1 | 4 | 4 | 4 | 4 |
| Maturidade ecossistema | 5 | 3 | 5 | 5 | 3 |

---

## 5. Stack recomendada

**Recomendação principal: .NET 9 + WPF (overlay + Ink/pointer) + SkiaSharp (composição/export) — com o *Core* isolado em biblioteca .NET Standard para migração futura a Avalonia.**

- **Linguagem/UI:** C# 13, .NET 9, WPF (XAML mínimo: 1 janela overlay por monitor + 1 toolbar).
- **Render tinta:** WPF `InkCanvas`/DirectX no MVP (menor latência, menos código); SkiaSharp como compositor offscreen para export PNG e para o futuro portável (mesmo modelo de stroke renderiza nos dois).
- **Interop Windows:** P/Invoke direto (`User32`, `DwmApi`) — sem dependência de terceiros para o crítico.
- **Settings:** JSON em `%AppData%` + `System.Text.Json` (sem ORM, sem SQLite no MVP).
- **Distribuição:** pasta portátil + instalador Inno Setup (assinado); MSIX opcional depois.
- **Plano B explícito:** se validação exigir Mac/Linux em < 12 meses, trocar a camada UI WPF → Avalonia mantendo `Core` + `Rendering.Skia` intactos (custo estimado: reescrever ~20–30% — só `Shell`/`Overlay`/`Toolbar`). Se o time domina Qt/C++, Qt 6 é substituto legítimo com o mesmo desenho de arquitetura.

**Por que não web (Electron/Tauri) como principal:** o caminho crítico (movimento do mouse → pixel) não pode pagar o pedágio do compositor Chromium + IPC JS quando a meta é < 16 ms com idle ~0%. A economia de dev web é real, mas cobra o preço exatamente no diferencial do produto.

---

## 6. Justificativa da stack (resumo executivo)

1. **Leveza:** sem Chromium embutido → atende RNF-01/02/07 por construção.
2. **Latência:** input pointer nativo + DirectX + repaint parcial → atende RNF-05/06.
3. **Overlay:** Win32 direto dá controle total de layered/click-through/Topmost/DPI — nenhuma abstração web entrega isso sem gambiarra.
4. **Velocidade de MVP:** WPF Ink + C# entregam caneta/linha/seta/undo em dias, não semanas.
5. **Portabilidade sem pagar agora:** Core desacoplado (sem `System.Windows`) permite Avalonia/Qt depois; decisão cara (engine) fica portável, decisão barata (janela) fica nativa.
6. **Contratação/suporte BR:** C#/.NET tem oferta ampla; Qt/C++ e Rust têm funil menor.

---

## 7. Arquitetura proposta

```
┌─────────────────────────────────────────────────┐
│ UI Shell (WPF): OverlayWindow xN, Toolbar, Tray │  ← só Win32+XAML aqui
├─────────────────────────────────────────────────┤
│ AppState (modo, ferramenta ativa, zoom, dirty)  │  ← single source of truth
├──────────┬──────────────┬───────────────────────┤
│ Tools    │ Input        │ Shortcuts/Config      │  ← comandos, sem desenho
├──────────┴──────────────┴───────────────────────┤
│ Document: strokes, undo/redo, serializer        │  ← portável, sem UI
├─────────────────────────────────────────────────┤
│ Rendering: ISkiaRenderer / InkAdapter, export   │  ← Skia portável + adaptador WPF
├─────────────────────────────────────────────────┤
│ Platform.Windows: layered/topmost/click-through │  ← P/Invoke isolado
│ hooks, monitores, DPI, single-instance          │
└─────────────────────────────────────────────────┘
```

**Regras de dependência:** `UI → AppState → Document/Tools`; `Rendering` e `Platform` não referenciam UI; `Document/Core` não referencia `System.Windows` nem Win32 (compila e testa sem janela). Isso é o que permite trocar WPF por Avalonia depois.

**Pastas (proposta inicial, enxuta):**
```
src/
  EpicPencil.Core/        # Stroke, Document, Undo/Redo, Tools (portável)
  EpicPencil.Rendering/   # IRenderer (Skia), export PNG/SVG futuro
  EpicPencil.Platform.Windows/ # overlay, DPI, hooks, tray helper
  EpicPencil.App/         # AppState, comandos, atalhos, config (WPF, sem XAML pesado)
  EpicPencil.Shell/       # XAML: OverlayWindow, Toolbar, App.xaml
tests/
  EpicPencil.Core.Tests/
docs/  prototypes/
```

**Sem overengineering:** sem DI container, sem MVVM framework, sem event bus no MVP — construtor + interfaces + 1 `AppState` observável bastam. Introduzir MediatR/Prism/ReactiveUI agora é custo sem benefício.

---

## 8. Modelo de dados do desenho

```csharp
// Pseudocódigo C# — portável (sem dependência WPF)
enum ToolKind { Pen, Pencil, Highlighter, EraserStroke, Line, Arrow, Rect, Ellipse }
record struct Pt(float X, float Y, float Pressure = 1f, long Ticks = 0); // coords em DIP lógicos
sealed class Stroke {
  Guid Id; ToolKind Tool; Rgba Color; float WidthDip; float Opacity;
  List<Pt> Points; // simplificados (RDP, tolerância ~0.75 DIP)
  Rect Bounds; // cache p/ hit-test + repaint parcial + borracha
  long CreatedAt; float ZoomApplied? // não — pontos sempre em coords de documento
}
sealed class Document {
  List<Stroke> Strokes; // ordem = z-order
  Camera Camera { X, Y, Zoom } // transform documento→tela
  UndoStack<ICommand> Undo, Redo;
}
interface ICommand { void Do(Document); void Undo(Document); } // AddStroke, EraseStroke(s), ClearAll
```

**Decisões-chave:**
- **Pontos em DIPs lógicos + `Camera{Zoom,Pan}` separada** → zoom sem perda (vetor) e sem reamostrar pontos.
- **Coalescing + simplificação RDP** na captura: `GetCoalescedEvents`/pointer coalesced + RDP moderado → stroke de 10 s cai de ~1200 para ~150–300 pontos sem degradação visível.
- **Bounds em cache por stroke** → borracha por toque, undo e repaint parcial ficam O(visíveis), não O(total).
- **Undo = pilha de comandos (Add/Erase/Clear)**, não snapshots de bitmap → memória O(operações), suporta milhares de strokes. Limite 100 passos + compactação.
- **Serialização:** JSON (camelCase, floats com 1–2 casas) para settings + sessão futura; PNG via Skia para export; SVG pós-MVP (strokes→paths, trivial a partir deste modelo).
- **Performance com N grande:** virtualização por viewport (só renderiza strokes cujo `Bounds` intersecta a dirty region); índice espacial simples (grid uniforme) só se perfilar e provar necessidade — não antes.
- **Pressão:** campo presente no modelo, ignorado no MVP (mouse não tem pressão); habilita tablet futuro sem migração.

---

## 9. Estratégia de renderização

1. **MVP (WPF):** stroke ativo = polyline `InkCanvas`/`DrawingVisual` com atualização incremental; strokes finalizados = visuals congelados (`Freeze`) — GPU faz o resto. Repaint **parcial** via dirty rect (união de `Bounds` do stroke ativo + últimos segmentos). Sem loop contínuo: `CompositionTarget.Rendering` só durante gesto ativo.
2. **Marca-texto:** pincel semi-transparente com modo de composição adequado (evita "escurecer" a cada overlap do próprio stroke: desenha o stroke em layer offscreen e compõe uma vez).
3. **Zoom:** `Camera.Zoom` como `ScaleTransform` da camada (vetor → sem serrilhado); texto/fundo abaixo não são afetados (overlay puro).
4. **Export/alternativo:** mesmo `Document` renderizado por SkiaSharp offscreen → PNG (MVP tardio) e SVG (futuro). Isso valida a portabilidade do Core desde o dia 1.
5. **Descartados no caminho crítico:** Canvas2D/WebGL-em-WebView (latência), GDI+ puro por software (CPU alto em 4K), re-render full-screen a cada mouse-move (mata bateria e 120 Hz).

---

## 10. Estratégia de input

| Entrada | API Windows | Uso |
|---------|-------------|-----|
| Mouse move/down/up, wheel | `WM_POINTER*` (preferido, com coalescing) + fallback `WM_MOUSE*` | Desenho, hover, pan (botão do meio / Espaço+arrasto), zoom (Ctrl+wheel) |
| Click esq/dir/meio | Pointer + `WM_*BUTTON*` | Desenhar / menu contexto toolbar / pan |
| Teclado local | Aceleradores WPF + `PreviewKeyDown` | P/E/H/L/S/U/Z/Y etc. |
| **Atalhos globais** | `RegisterHotKey` (MVP, poucos IDs) → `SetWindowsHookEx(WH_KEYBOARD_LL)` pós-MVP se precisar de combos complexos | Toggle desenho, undo, limpar, esconder (funcionam com outro app focado) |
| Tablet/pressão (futuro) | Windows Ink (`RealTimeStylus`) / WinTab | Só plugar `Pressure` no `Pt` — modelo já pronto |
| Single-instance | Mutex + `WM_COPYDATA`/pipe | Abrir 2ª vez foca a toolbar existente |

**Modos (coração da UX):**
- **Modo Desenhar:** overlay recebe mouse (não click-through), cursor em anel, atalhos locais ativos, toolbar visível.
- **Modo Interagir:** overlay `WS_EX_TRANSPARENT` (click-through total — mouse "atravessa" para o app abaixo), tinta continua visível, toolbar colapsada/tray; 1 hotkey global volta ao desenho.
- Toggle: botão da toolbar + `Alt+D` (global, configurável) + clique do meio opcional. Indicador inequívoco (borda sutil ou ponto colorido no canto) para o usuário nunca se perguntar "por que não desenha?".

---

## 11. Estratégia de overlay (Windows)

- **Janela:** 1 `OverlayWindow` fullscreen borderless por monitor (`WS_POPUP`, `WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, sem sombra/borda, `ShowInTaskbar=false`), cobrindo o `rcWork` do seu monitor (work area — **revisado de `rcMonitor` para `rcWork`**: a taskbar/appbars são área de interação do shell e o overlay nunca as cobre, REQ1–REQ3).
- **Transparência:** layered window com alfa por pixel (`UpdateLayeredWindow` / composição DWM); fundo 100% transparente, só tinta + (opcional) cursor têm alfa.
- **Always-on-top:** `SetWindowPos(HWND_TOPMOST)` + watchdog (timer 500 ms–1 s reasserindo se um app fullscreen roubar z-order; sem busy-loop).
- **Click-through:** liga/desliga `WS_EX_TRANSPARENT` via `SetWindowLongPtr` + `SetWindowPos(FRAMECHANGED)` — transição instantânea, sem recriar janela.
- **Foco:** overlay nunca rouba foco ao entrar em modo interagir (`SW_SHOWNOACTIVATE`); toolbar é a única janela focável.
- **Fundo acelerado por GPU / jogos fullscreen exclusivo:** overlay Topmost funciona sobre a maioria dos apps borderless/janela; **fullscreen exclusivo (DXGI flip exclusivo) pode ocultar qualquer overlay** — documentar como limitação conhecida (workaround futuro: modo "captura" que congela e anota sobre screenshot).
- **Sem captura de tela no MVP:** overlay puro (ao vivo). Captura só entra como *modo opcional futuro* (congelar → anotar → exportar), pois muda a promessa do produto.

---

## 12. Estratégia de multi-monitor e DPI

- **Topologia:** enumera com `EnumDisplayMonitors`/`Screen.AllScreens`; 1 overlay por monitor; evento `DisplaySettingsChanged`/`WM_DISPLAYCHANGE` recria/reposiciona em runtime (plug/unplug, troca de primário, sleep/wake).
- **DPI:** manifesto `PerMonitorV2`, todos os pontos em DIPs, conversão DIP↔pixel por monitor (`GetDpiForMonitor`); testa matriz: 100%+100%, 100%+150%, 150%+200%, 4K+1080p, ordem esq/dir diferente.
- **Zoom/pan:** `Camera` **por documento virtual unificado** (coords de desktop virtual) ou por monitor? Recomendado: **documento em coords virtuais + Camera global** (pan/zoom consistentes ao arrastar a toolbar entre monitores), render por janela aplicando offset do monitor. Mais simples de raciocinar e de serializar.

---

## 13. UX/UI proposta

**Escolha: toolbar flutuante compacta + tray + hotkeys (descartar menu radial e toolbar fixa superior no MVP).** Motivo: flutuante acompanha o apresentador entre monitores, some com 1 tecla, tem 1–2 cliques para tudo; radial é legal mas tem curva de aprendizado e custo de implementação; fixa superior rouba espaço e quebra em ultrawide.

**Layout da toolbar (horizontal, ~380×48, draggable, com pin):**
```
[✏️ Pen] [✏️ Pencil] [🖍️ Marker] [📏 Linha] [➡️ Seta] | [● cores ×6 + 🎨] | [━ S M L] | [↩️ ↪️] [🧹] [👆/🖱️ modo] [— 👁️ ✖️]
```
- Linha/Retângulo/Elipse em *flyout* do botão 📏 (secundárias a 1 clique extra).
- Estado ativo: pill destacado + anel do cursor reflete cor/espessura.
- Feedback: cursor em anel calibrado (raio = espessura/2 × zoom); preview elástico de linha/seta; toast mínimo ao limpar ("Tinta apagada — Ctrl+Z para desfazer").
- Borracha: padrão por stroke (rápida); tooltip explica; modo área vem depois.
- Cores: 6 presets (preto, branco, vermelho, azul, verde, amarelo-marker) + picker custom; branco/preto incluídos porque fundo varia.
- Espessura: S/M/L por ferramenta (memorizados) + slider fino em popover (pós-MVP pode ser só slider).
- Esconder: `F9` ou botão 👁️ esconde tinta; `F10`/tray esconde toolbar; `Esc` sai do modo desenho (nunca apaga nada).
- Entrar/sair: `Alt+D` alterna desenho/interagir; duplo-clique no tray abre toolbar; `Esc` sempre volta a estado seguro.

**Acessibilidade:** todos os botões com tooltip + atalho visível; foco por teclado; alto contraste respeitando tema Windows.

---

## 14. Sistema de atalhos (proposta inicial)

| Ação | Local | Global (MVP) | Observação |
|------|-------|--------------|------------|
| Alternar desenho/interagir | `Alt+D` | `Alt+D` ✅ | O mais importante; global desde o MVP |
| Caneta / Lápis / Marker | `P` / `B` / `H` | — | `B` = pencil (padrão tipo Photoshop); documentar |
| Borracha | `E` | — | |
| Linha / Seta | `L` / `S` (ou `A`) | — | `S` de "seta"? Avaliar conflito com idioma; deixar configurável |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y`, `Ctrl+Shift+Z` | `Ctrl+Z` global* ✅ | *Global com cuidado: só quando em modo desenho para não roubar undo de outros apps — ver risco R-07 |
| Limpar tudo | `C` ou `Del` (com undo) | hotkey dedicado ✅ | Evitar `Del` global |
| Esconder tinta / toolbar | `F9` / `F10` | ✅ | |
| Espessura S/M/L | `1` / `2` / `3` | — | |
| Pan temporário | segurar `Espaço` | — | Padrão Figma/PS |
| Zoom camada | `Ctrl+Wheel`, `Ctrl+0` reset | — | Aplica à tinta, não ao app abaixo (comunicar!) |

**Regras:** atalhos de 1 tecla só quando overlay focado (modo desenho); globais = no máximo 4–5 combos com modificador; tudo configurável a partir da fase 2 (UI simples de remapeamento + detecção de conflito). Não usar `P/B/E` globais — sequestrariam digitação em outros apps.

---

## 15. Persistência — o que entra no MVP e o que não entra

**MVP (necessário):** `settings.json` em `%AppData%/EpicPencil/` — última ferramenta, cor/espessura por ferramenta, posição da toolbar, atalhos globais, monitor preferido, flags (watchdog, DIPs). Escrita debounced + backup `.bak` (evita corrupção em queda de energia).
**MVP tardio (incluir se couber):** exportar PNG da tinta+ bounds (via Skia offscreen) — custo baixo, valor alto para aulas/prints.
**Pós-MVP:** salvar/restaurar sessão (`.epencil.json` com Document + Camera), PNG com fundo transparente vs. composited, SVG, cores favoritas, histórico de sessões recentes.
**Não fazer:** SQLite/ORM, conta, nuvem, telemetria com rede no MVP (ver §17/§20-privacy).

---

## 16. MVP — escopo fechado (com ajustes críticos à lista original)

**IN (8–10 semanas, 1 dev focado):**
1. Overlay transparente fullscreen por monitor + Topmost + click-through alternável ✅
2. Caneta, marca-texto, (lápis = preset de caneta com textura simples — ver ADR-05) ✅
3. Linha reta + seta (preview elástico) ✅
4. Borracha por stroke + limpar tudo (com undo) ✅
5. Cores (6 presets + custom) + espessura S/M/L memorizada por ferramenta ✅
6. Undo/redo (pilha de comandos) ✅
7. Toolbar flutuante + tray + modos desenho/interagir + esconder tinta ✅
8. Hotkeys: 4–5 globais + locais de 1 tecla ✅
9. Multi-monitor + PerMonitorV2 DPI ✅
10. Settings JSON + single-instance + startup rápido ✅

**OUT (explicitamente fora, para proteger o MVP):** borracha por área, formas (exceto linha/seta), seleção/mover, zoom com captura de tela, tablet pressure real, export SVG, auto-update, MSIX/Store, remapeamento total de atalhos (básico entra), onboarding animado.

**Questionamentos à lista original:** (a) "Lápis" como engine separada é luxo — MVP aprova *preset*; (b) "Zoom" entra como zoom da camada (Ctrl+wheel) + reset, não como lupa do desktop; (c) "Pan" entra mínimo (Espaço+arrasto); (d) Export PNG sobe para MVP tardio por custo/benefício.

---

## 17. Roadmap

| Fase | Objetivo | Saída validável | Duração* |
|------|----------|-----------------|----------|
| **0 — Spikes** | Protótipos P1–P6 (§22) | 6 protótipos + decisão WPF vs Avalonia carimbada | 1–2 sem |
| **1 — Core** | Document, Stroke, RDP, Undo, serializer, testes | `Core` com >80% coverage, benchmark 10k strokes | 1–2 sem |
| **2 — Overlay Win** | Janela layered/Topmost/click-through, multi-mon, DPI | App "Hello Ink": risca sobre o navegador | 2 sem |
| **3 — Engine tinta** | Pen/marker/linha/seta, borracha stroke, dirty-rect, cursor anel | Latência < 16 ms medida | 2 sem |
| **4 — Toolbar/Tray/Modos** | Toolbar flutuante, tray, modos, esconder | Fluxo 6-passos do §2 em < 10 s | 1–2 sem |
| **5 — Atalhos/Settings** | Locais+4 globais, settings JSON, single-instance | Sem conflito com apps comuns | 1 sem |
| **6 — Endurecer** | Watchdog z-order, sleep/wake, fullscreen, 4K, testes beta | Matriz de compatibilidade verde | 1–2 sem |
| **7 — MVP release** | Inno Setup assinado, PNG export, docs, atalhos básicos | Instalador < 15 MB, startup < 500 ms | 1 sem |
| **8+ — Pós-MVP** | Área-erase, formas, sessão save, SVG, remapeamento total, Avalonia/Mac, tablet pressure, modo captura-congelada, auto-update | Releases incrementais | — |

*Estimativas para 1 dev sênior focado; dobrar se parcial.

---

## 18. Estrutura do projeto (enxuta, justificada)

```
epic-pencil/
  docs/ARCHITECTURE.md            # este documento
  prototypes/P1-overlay/ ...      # spikes descartáveis (não entram no src)
  src/
    EpicPencil.Core/              # Stroke/Document/Undo/RDP — ZERO dependência UI/Win32
    EpicPencil.Rendering/         # IRenderer Skia + export PNG (portável)
    EpicPencil.Platform.Windows/  # P/Invoke, OverlayCtrl, Hotkeys, Monitors, Dpi
    EpicPencil.App/               # AppState, comandos, config, atalhos (testável sem janela)
    EpicPencil.Shell/             # WPF: OverlayWindow, Toolbar, Tray, App.xaml, manifesto DPI
  tests/EpicPencil.Core.Tests/    # undo, RDP, serializer, bounds, viewport culling
  installer/inno/setup.iss        # Inno Setup (portátil + instalador)
  .github/workflows/ci.yml        # build + testes + medida de binário
```

**Responsabilidades:** Core nunca importa UI; Platform nunca importa UI; App orquestra; Shell só desenha e repassa input. Regra de ouro: *se precisa de `System.Windows` ou `DllImport`, não mora no Core.*

---

## 19. Architecture Decision Records (ADRs)

**ADR-01 — Sem stack web no caminho crítico.**
Alternativas: Electron / Tauri / Nativo. Escolha: nativo (.NET). Motivo: RNFs de RAM/startup/latência são incompatíveis com Chromium embutido/WebView no loop de tinta. Trade-off: abre mão de dev web rápido e cross-platform imediato.

**ADR-02 — WPF agora, Core portável para Avalonia depois.**
Alternativas: Qt agora / Avalonia agora / WPF-para-sempre. Escolha: WPF no MVP + Core sem `System.Windows`. Motivo: menor risco de overlay no Windows com rota de fuga barata (~20–30% para portar só Shell). Trade-off: retrabalho futuro da Shell se portar; mitigado pelo isolamento.

**ADR-03 — Overlay puro (sem captura) no MVP.**
Alternativas: congelar screenshot / espelhar DXGI. Escolha: overlay ao vivo. Motivo: preserva interação com o app abaixo (a promessa do produto); captura congela e quebra demos ao vivo. Trade-off: sem zoom do conteúdo alheio e sem tinta sobre fullscreen exclusivo; modo captura vira feature futura opt-in.

**ADR-04 — Uma janela overlay por monitor (não spanning).**
Alternativas: 1 janela gigante cobrindo desktop virtual. Escolha: N janelas. Motivo: DPI por monitor, plug/unplug, offsets negativos e `rcMonitor` exato ficam triviais; spanning quebra em DPI misto. Trade-off: N handles + sincronia de Camera (resolvido com documento virtual único).

**ADR-05 — Lápis = preset no MVP, engine separada depois.**
Alternativas: 3 engines desde o dia 1. Escolha: Pen com parâmetros (granulado pode ser só ruído de alfa barato). Motivo: diferenciação real de lápis exige shader/textura e testes; valor marginal no MVP. Trade-off: revisitar se beta pedir.

**ADR-06 — Undo por comandos, não por snapshots.**
Alternativas: bitmap history / snapshots. Escolha: pilha Add/Erase/Clear. Motivo: memória O(op) e precisão vetorial; snapshots estouram RAM em 4K. Trade-off: Clear+Undo de milhares de strokes precisa de lista (ok — é só referência).

**ADR-07 — Pontos em DIP + RDP + bounds em cache.**
Alternativas: pixels brutos / todos os pontos. Escolha: DIPs simplificados. Motivo: DPI-independência, zoom sem perda, hit-test e repaint O(visíveis). Trade-off: tolerância RDP precisa de tuning perceptivo (teste A/B com desenho rápido).

**ADR-08 — Settings em JSON, sem DB.**
Alternativas: SQLite / Registry / roaming cloud. Escolha: JSON local + `.bak`. Motivo: suficiente, debugável, sem dependências. Trade-off: sem query; irrelevante nesta escala.

**ADR-09 — Distribuição portátil + Inno Setup assinado (MSIX depois).**
Alternativas: só MSIX / Store / winget-only. Escolha: exe portátil + instalador clássico. Motivo: utilitário precisa de "baixar e rodar" sem fricção de Store; MSIX adiciona restrições de hooks/Topmost a gerenciar. Trade-off: auto-update manual na v1 (link + verificação de versão).

**ADR-10 — Sem telemetria com rede no MVP.**
Alternativas: telemetry desde o dia 1. Escolha: métricas locais (contadores) + log local opt-in. Motivo: privacidade como feature; SmartScreen/defender já geram atrito suficiente. Trade-off: menos dados de campo (compensado com beta fechado).

---

## 20. Riscos técnicos + como validar (spike antes de investir)

| ID | Risco | Impacto | Validação (protótipo) |
|----|-------|---------|------------------------|
| R-01 | Overlay some atrás de app elevado/fullscreen exclusivo | Alto | P1+P4: Topmost sobre VS admin, jogos borderless vs. exclusivo; documentar limite |
| R-02 | Click-through com N monitores/DPI misto | Alto | P3+P4: toggle 100×, mede tempo (< 50 ms) e região correta por monitor |
| R-03 | Latência tinta > 16 ms em 4K/120 Hz | Alto | P2+P6: mouse rápido + dirty-rect + coalesced events; mede com timestamps |
| R-04 | Foco/roubo de teclado (atalhos somem, app abaixo perde foco) | Médio-Alto | P5: matriz de foco × modo; `ShowNoActivate` auditado |
| R-05 | HiDPI borrado/deslocado | Médio-Alto | P4: matriz 100/125/150/200% mista; régua de calibração DIP↔px |
| R-06 | CPU idle > 1% (timers, blur, animação) | Médio | P2: ETW/perf counter 5 min idle; proíbe loop contínuo e backdrop blur no MVP |
| R-07 | Hotkey global rouba Ctrl+Z de outros apps | Alto | P5: global só em modo desenho OU só combos com Alt/F9; teste em Word/VSCode |
| R-08 | SmartScreen/Defender bloqueia instalador sem assinatura | Médio | Assinar EV/code-sign + teste em VM limpa; página de download com hash |
| R-09 | GPU alheia (overlay + vídeo/jogo) com tearing | Médio | P1: teste YouTube 4K + Meet + jogo borderless; fallback: reduzir alfa/efeitos |
| R-10 | Vazamento GDI/handles com milhares de strokes | Médio | P6: soak 10k strokes + handles/RAM sobe-e-desce após Clear+Undo |

---

## 21. Protótipos necessários (ordem ideal — cada um ≤ 2 dias)

1. **P1 — Janela fantasma:** fullscreen transparente Topmost sobre navegador/YouTube; valida layered + DWM + z-order.
2. **P2 — Primeiro risco:** 1 stroke polyline com dirty-rect + coalescing; mede latência e CPU em rabisco rápido.
3. **P3 — Click-through:** toggle `WS_EX_TRANSPARENT` desenho↔interagir; mede tempo e corretude do hit (inclui botão "atravessável?" clicando no app abaixo).
4. **P4 — Multi-monitor + DPI:** N janelas, plug/unplug, matriz de escalas; régua de calibração.
5. **P5 — Hotkeys:** `RegisterHotKey` globais mínimas + locais; matriz de foco e teste de não-sequestro de Ctrl+Z.
6. **P6 — Soak:** gera 10k strokes sintéticos + Clear/Undo; mede FPS, RAM, handles, tempo de export PNG.
- **Ordem lógica:** P1→P2→P3 (núcleo da promessa) → P4→P5 (compatibilidade) → P6 (teto). Só após P1–P3 verdes a decisão WPF-vs-Avalonia é carimbada (se WPF travar em P3/P4, repete P1–P3 em Avalonia antes da Fase 1).

---

## 22. Estratégia de testes

- **Unitários (xUnit, Core/App):** RDP (preserva forma, reduz pontos), undo/redo (sequências + Clear), bounds/hit-test, serializer round-trip, viewport culling. Meta: >80% no Core.
- **Propriedade/fuzz:** gera polilinhas aleatórias → invariantes (bounds contém pontos; undo retorna ao estado).
- **Performance (CI + local):** benchmarks BenchmarkDotNet: adicionar 10k strokes, hit-test, export PNG 4K; falha se regressão >15%. Medida de binário/instalador no CI (orçamento: <15 MB).
- **Manuais guiados (matriz):** monitores/DPI, sleep/wake, troca de primário, apps elevados, fullscreen borderless/exclusivo, YouTube 4K, Teams/Meet screen-share (tinta aparece? — depende de captura DXGI do app de reunião, documentar), tablet futuro.
- **Beta:** anel interno → 10–20 apresentadores/professores com log local opt-in; critérios de saída: 0 crashes/100h, latência mediana <16 ms, NPS "voltaria a usar".
- **Sem E2E UI pesado no MVP:** FlaUI/WinAppDriver só pós-MVP; teste manual guiado + checklist valem mais agora.

---

## 23. Benchmark — metas vs. referências

| Métrica | Meta MVP (obrigatória) | Alvo de excelência | Como medir |
|---------|------------------------|--------------------|------------|
| Startup frio | < 800 ms | < 300 ms | `QueryPerformanceCounter` clique→primeiro frame; média 5 runs |
| RAM idle | < 80 MB | < 35 MB | Working set privado após 5 min idle |
| CPU idle | < 1% | ~0% | ETW / Task Manager 5 min |
| CPU desenhando | < 15% | < 8% | Rabisco circular 30 s em 1080p |
| Latência | < 24 ms p50 | < 8 ms p50 | Timestamp pointer→present |
| FPS stroke | ≥ 60 | ≥ 120 (em tela 120 Hz) | `CompositionTarget` / PresentMon |
| Instalador | < 25 MB | < 10 MB | Artefato CI |
| Soak 10k strokes | sem travar; pan/undo < 100 ms | < 50 ms | P6 automatizado |

*Números acima de "meta" são teto (não pode passar); "alvo" é aspiração. Hardware de referência: i5 11ª gen / 8 GB / SSD / 1080p + 4K secundário. Benedict Wolfe é referência qualitativa: abrir e riscar precisa parecer instantâneo — se o protótipo não parecer, a stack está errada.*

---

## 24. Próximos passos (sem escrever produção ainda)

1. **Aprovar este documento** + travar ADR-01…ADR-10 (ou registrar divergência).
2. **Executar P1→P3 (≤ 1 semana):** carimba WPF vs. Avalonia e valida a promessa central.
3. **Criar esqueleto da solução** (`Core` + testes + CI de build/medida) — sem features, só vertical slice compilável.
4. **Revisão de escopo MVP** após P1–P6: confirma IN/OUT do §16 com dados, não opinião.
5. **Só então Fase 1** (engine). Qualquer incerteza remanescente vira spike time-boxed (≤2 dias), nunca "implementação esperançosa".

**Incertezas declaradas:** (a) Avalonia como substituto direto depende de P1–P3; (b) tinta sobre fullscreen exclusivo provavelmente será limitação documentada, não bug a corrigir no MVP; (c) diferenciação real de lápis/marker depende de tuning perceptivo no P2.

---

*Fim do documento. Próxima ação aguardada: aprovação para iniciar Protótipos P1–P3.*
