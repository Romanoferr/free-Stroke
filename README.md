# Epic Pencil (free-Stroke)

Aplicativo desktop Windows, leve e offline-first, para anotação/desenho sobre a tela
(overlay transparente estilo Epic Pen, implementação própria). Núcleo de desenho
portável + overlay WPF/Win32 no Windows.

> Documentos de arquitetura (pré-implementação, contexto histórico):
> `docs/ARCHITECTURE.md` e `docs/ARCHITECTURE-REVIEW.md`.

## Requisitos

| Item | Obrigatório | Versão / nota |
|---|---|---|
| SO | Sim | **Windows 10/11 x64** para `EpicPencil.Shell` e `prototypes/S1.Overlay` (WPF `net9.0-windows`). `EpicPencil.Core` e os testes (`net9.0`) compilam em qualquer SO com SDK |
| .NET SDK | Sim | **SDK 9.x** (`global.json` fixa `9.0.100` com `rollForward: latestFeature`). Testado com **9.0.318**. SDK 10 compila, mas o alvo oficial é o 9 |
| Runtime | Automático | Instalado junto com o SDK 9 (`Microsoft.WindowsDesktop.App 9.x` para WPF). Sem SDK, só runtime **não basta** |
| NuGet | Automático | Pacotes de teste (`xunit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`) baixados do nuget.org no `restore`. Sem conta, sem feed privado |
| Rede | Não | Projeto 100% offline após o SDK instalado (exceto o `restore` inicial do NuGet) |
| Banco de dados / APIs / serviços externos | Não | Não há backend, banco, cloud, auth ou telemetria com rede |
| Variáveis de ambiente / `.env` | Não | **Zero variáveis de ambiente consumidas.** Não existe `.env` nem `.env.example` por não haver nada a configurar |
| Assets externos | Não | Sem imagens, ícones, fontes ou binários externos. XAML usa só a fonte de sistema `Consolas`. Sem downloads manuais |

Escrita em disco em runtime (criado automaticamente, sem configuração):

- Logs de diagnóstico: `%LocalAppData%\EpicPencil\Logs\yyyyMMdd.log` (ex.: `C:\Users\<você>\AppData\Local\EpicPencil\Logs\`).

## Instalação (desde um clone limpo)

```powershell
git clone <repository-url>
cd free-Stroke

# 1. Instale o .NET SDK 9 (Windows, requer admin para o instalador):
winget install --id Microsoft.DotNet.SDK.9 --silent --accept-package-agreements --accept-source-agreements

# 2. Confirme (nova janela do PowerShell após instalar, para recarregar o PATH):
dotnet --list-sdks
# esperado: 9.0.xxx [C:\Program Files\dotnet\sdk]

# 3. Restaure + compile a solução:
dotnet build EpicPencil.sln --nologo -v minimal
# esperado: "Compilação com êxito. 0 Aviso(s) 0 Erro(s)"

# 4. Rode os testes:
dotnet test EpicPencil.sln --nologo
# esperado: "Aprovado! – Com falha: 0, Aprovado: 35, ..." (contagem pode crescer)
```

> `prototypes/S1.Overlay` **não** faz parte do `.sln` (é spike descartável). Para compilá-lo:
> `dotnet build prototypes/S1.Overlay/S1.Overlay.csproj`

## Configuração

Nada a configurar. Não há `.env`, `appsettings.json`, connection strings,
segredos ou `settings.json` obrigatório — o schema de preferências
(`src/EpicPencil.Core/Settings.cs`, `SettingsV1`) é um modelo em código com
defaults e `Normalize()`; nenhum arquivo de settings é lido/escrito pelo app
nesta versão (documentos descrevem o `settings.json` futuro em
`%AppData%/EpicPencil/` como plano, não implementado).

## Toolbar (UI)

Cartão flutuante sem chrome do Windows (cantos arredondados, translúcido),
arrastável pelo grip (⋮⋮) para qualquer posição da tela:

- **Recolher/expandir** (chevron no cabeçalho, ~130 ms): recolhida vira uma
  pílula mínima (grip + cor ativa + ferramenta + ponto de modo + expandir).
- **Ferramentas em ícones vetoriais** (sem dependências externas) com tooltip +
  atalho; ferramenta ativa com destaque azul; hover/pressed sutis.
- **Cor e espessura**: 6 swatches circulares (anel branco = selecionada) +
  slider de espessura 1–10 (presets maiores, ex. marca-texto, pinam no máximo
  até o primeiro ajuste).
- **Ações**: desfazer/refazer, apagar captura, limpar tudo, olho
  (mostrar desenho = modo desenho / ocultar = interagir com a tela),
  pílula Sair; diagnósticos ocultos em produção (código mantido p/ debug).
- Mostrar/ocultar também pelo dot verde/laranja da barra recolhida e pelos
  atalhos locais **PgUp**, **`'`** (aspas) ou **F9** — funcionam com a toolbar
  recolhida (mesma janela); **Esc** oculta e libera o mouse.
- Hotkeys locais inalterados (P/B/H/L/S/V/E, C, Ctrl+Z/Y).

## Multi-monitor

Suportado: o app enumera todos os monitores via `EnumDisplayMonitors` e cria
**1 overlay por work area** (1, 2 ou 3+ monitores — o primário mantém o
comportamento atual). O documento é compartilhado no espaço **global da tela
virtual (pixels físicos, origem negativa preservada)**; cada overlay converte
coords na borda (input local→global, visual global→local).

- Identificação: `src/EpicPencil.Windows/MonitorLayout.cs` (primário = Id 0,
  demais por dispositivo); monitor ativo por ponto via `MonitorFromPoint`.
- Conversão: `src/EpicPencil.Core/MonitorSpace.cs` (`MonitorFrame` — matemática
  pura, coberta por `MonitorSpaceTests`).
- Captura: `BitBlt` já opera em coords virtuais (negativas OK); o flow recebe
  px globais, sem matemática de DPI.
- Toolbar: única e compartilhada (owner = overlay primário); **Flash** pisca
  todos os overlays, **Trazer p/ frente** reasserte todos, status mostra `mons=N`.
- Topologia (plug/unplug/resolução/escala/primário): cada overlay observa
  `WM_DISPLAYCHANGE` e o app reconstrói os overlays com debounce de 800 ms,
  mantendo o documento. Fechar qualquer janela encerra tudo (sem órfãs).
- DPI: app `PerMonitorV2`; cada overlay mede sua escala real
  (`VisualTreeHelper.GetDpi`) e posiciona via `SetWindowPos` físico (exato com
  DPI misto). Com DPIs diferentes, tamanho de traço entre monitores pode variar
  levemente (largura é guardada no frame de origem) — aproximação documentada.
- Strokes multi-monitor: o gesto pertence ao overlay onde começou (o capture do
  mouse roteia o resto do gesto para lá) — sem comportamento estranho ao
  atravessar monitores.
- Validação: `dotnet run --project src/EpicPencil.Shell -- --selftest`
  (seção `RunMonitors`: enumeração == `SM_CMONITORS`, `MonitorFromPoint` com
  coords negativas, stroke global negativo, BitBlt real no secundário).

Limitações que permanecem: UIPI (não cobre apps elevados), fullscreen exclusivo
DXGI oculta qualquer overlay, Teams/Meet/Zoom podem não capturar a tinta.

## Execução

```powershell
# App principal (abre overlay de desenho em work area + toolbar flutuante):
dotnet run --project src/EpicPencil.Shell/EpicPencil.Shell.csproj

# Self-test headless do app (valida canvas, undo, borracha, captura BitBlt,
# work area, ownership toolbar — sem precisar interagir com a UI):
dotnet run --project src/EpicPencil.Shell/EpicPencil.Shell.csproj -- --selftest

# Self-test do protótipo S1 (overlay puro: HWND, toggle click-through, foco):
dotnet run --project prototypes/S1.Overlay -- --selftest

# Protótipo interativo (janela clicável; para sair use taskkill pois o modo
# click-through é intencionalmente não-clicável):
dotnet run --project prototypes/S1.Overlay
taskkill /IM S1.Overlay.exe /F
```

Sequência completa de validação (equivalente a `.opencode/command/validate.md`):

```powershell
dotnet build EpicPencil.sln --nologo -v minimal
dotnet test EpicPencil.sln --nologo
dotnet build prototypes/S1.Overlay/S1.Overlay.csproj --nologo -v minimal
dotnet run --project prototypes/S1.Overlay --no-build -- --selftest
dotnet run --project src/EpicPencil.Shell/EpicPencil.Shell.csproj --no-build -- --selftest
# Pureza do Core (esperado: só 1 hit, comentário em ToolKind.cs declarando a regra):
Select-String -Path src/EpicPencil.Core/*.cs -Pattern "System\.Windows|DllImport|Presentation"
```

## Build de produção

```powershell
# Release local (solução):
dotnet build EpicPencil.sln -c Release --nologo -v minimal

# Publicar o app Windows autocontido ou dependente do runtime:
dotnet publish src/EpicPencil.Shell/EpicPencil.Shell.csproj -c Release -o publish/
```

Não há instalador (`installer/inno/setup.iss` citado nos docs **não existe** no
repo), CI (`.github/workflows/ci.yml` citado nos docs **não existe**), nem
projetos `Rendering`/`Export.Skia` (citados nos docs como proposta, **não
implementados** — `ExportJob.cs` é só a definição pura do job). Limitações
documentadas, não bugs a corrigir neste escopo.

## Troubleshooting

| Sintoma | Causa | Solução |
|---|---|---|
| `No SDKs were found` no `dotnet --info` | Só runtimes instalados, sem SDK | Instale o SDK 9 via winget (seção Instalação) e abra nova janela do PowerShell |
| `error NETSDK1045: ... net9.0 ... SDK 8` ou SDK errado assumido | `dotnet` resolvendo SDK antigo/mais novo | `global.json` fixa o 9; rode `dotnet --list-sdks` e garanta um `9.0.xxx` instalado |
| `dotnet run ... S1.Overlay.exe ... não pode encontrar o arquivo` | Protótipo fora do `.sln`, nunca compilado | Compile primeiro: `dotnet build prototypes/S1.Overlay/S1.Overlay.csproj`, depois `run` (com ou sem `--no-build`) |
| App abre mas só 1 overlay com 2+ monitores | Enumeração falhou ou rebuild em curso | Confira o log: linha `topologia: N monitor(es)`; `overlay mon=I` deve aparecer 1× por monitor |
| Tinta não aparece sobre app elevado (VS como admin, Task Manager) | UIPI: app não-elevado nunca fica acima de app elevado | Limitação documentada — não rode o app como admin como workaround |
| Tinta some em jogo fullscreen exclusivo / não aparece no Teams/Meet/Zoom | Fullscreen exclusivo (DXGI) oculta qualquer overlay; captura por janela pode ignorar layered/Topmost | Limitação documentada; modo "captura-congelada" é pós-MVP |
| Cliques não chegam ao overlay | Janela coberta (z-order) ou modo interagir (click-through) ativo | Use os botões de diagnóstico da toolbar: **Flash** (vermelho 400 ms = janela presente/no topo), **Who** (dono do HWND sob o cursor), **Front** (reassert Topmost). Logs em `%LocalAppData%\EpicPencil\Logs\` registram `ex=... transparent=...` a cada `ApplyState` |
| `git status` mostra `bin/`/`obj/` | Nunca deve acontecer (`.gitignore` cobre) | Estão ignorados; se aparecerem, não commite — verifique o `.gitignore` |

## Estrutura

```
EpicPencil.sln                  # Core + Windows + Shell + Core.Tests (protótipo fora do sln)
global.json                     # fixa SDK 9 (rollForward latestFeature)
src/EpicPencil.Core/            # net9.0 puro: Stroke/Document/Undo/RDP/HitTest/Settings/MonitorSpace (sem System.Windows/DllImport)
src/EpicPencil.Windows/         # net9.0-windows: Native (P/Invoke user32/gdi32/shcore), OverlayBehavior, MonitorLayout/MonitorInfo, ScreenCapture (BitBlt), Log
src/EpicPencil.Shell/           # WPF PerMonitorV2: OverlayWindow (1/monitor), ToolbarWindow (única), InkSurface, AppState, WpfStrokeRenderer
tests/EpicPencil.Core.Tests/    # xUnit (35 testes, incl. MonitorSpaceTests)
prototypes/S1.Overlay/          # spike S1 (overlay puro + self-test)
docs/                           # ARCHITECTURE.md + ARCHITECTURE-REVIEW.md (contexto)
```
