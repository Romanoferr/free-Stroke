# Epic Pencil — Estratégia de distribuição (Windows x64)

> Versão atual: **0.1.0** (fonte única: `<Version>` em `Directory.Build.props`).
> Última revisão: 2026-09-15.

## 1. Auditoria do projeto (resumo do que foi levantado antes de mudar nada)

| # | Item auditado | Situação encontrada |
|---|---|---|
| 1 | Target frameworks / RIDs | `Core` = `net9.0` puro; `Windows` e `Shell` = `net9.0-windows` com `UseWPF` só na Shell. Nenhum `RuntimeIdentifier` fixado (correto: dev loop segue AnyCPU; RID só no publish). |
| 2 | Build/publish | Sem props de publish; `dotnet publish` padrão geraria app **framework-dependent** (exigiria runtime). Corrigido via `PublishSingleFile`/`PublishTrimmed=false` na Shell + script. |
| 3 | Nativas / P/Invoke | Só `user32.dll`, `gdi32.dll`, `shcore.dll` (todas do próprio Windows, sem DLLs de terceiros). `GetDpiForMonitor` tem fallback 96 se `shcore` falhar. OK para single-file. |
| 4 | Arquivos em runtime | Fonte `SpaceMono` vai como `Resource` (embutida no assembly, sem dependência do Windows do usuário). `Fonts/OFL.txt` vai como `Content` (copiado ao lado do exe — licença acompanha). Sem `appsettings.json`, sem `.env`, sem assets externos. |
| 5 | AppData | Só escrita: `%LocalAppData%\EpicPencil\Logs\yyyyMMdd.log` (classe `Log`, thread-safe, nunca derruba o app). Nenhum settings lido/escrito nesta versão (`SettingsV1` é só modelo em memória). |
| 6 | Recursos WPF | `Theme.xaml`, `ToolbarWindow.xaml`, `OverlayWindow.xaml` + fonte embutida — todos `Resource`/compilados, funcionam dentro do single-file (validado com `--selftest` no exe publicado). |
| 7 | Startup | `App.OnStartup` → `Log.Init()` → `--selftest` ou `RebuildOverlays()` (enumera monitores, 1 overlay por work area + toolbar owned). Sem rede, sem migração, sem janela de login: primeira execução = execução normal. |
| 8 | Tray / hooks / janelas | **Não há** tray icon, hook global de teclado/mouse nem `RegisterHotKey` nesta versão (hotkeys são locais da toolbar; veto de arquitetura mantido). Fechar qualquer janela = shutdown completo do processo. Nada roda em background. |
| 9 | Permissões | App roda como usuário padrão: escreve só em `LocalAppData` + PNGs onde o usuário escolher (diálogo padrão). UIPI: não cobre apps elevados; fullscreen exclusivo DXGI oculta o overlay (limitações documentadas, sem workaround com admin). |
| 10 | Windows limpo sem .NET | Resolvido com publish **self-contained win-x64** (~130 MB): testado o exe publicado com `--selftest` = `SHELL SELFTEST OK`, exit 0. |
| 11 | Caminhos absolutos / máquina de dev | Nenhum encontrado: pack URI usa nome do assembly (`EpicPencil.Shell`), fontes/log usam pastas relativas ou `SpecialFolder`. Por isso o `AssemblyName` foi **mantido** como `EpicPencil.Shell` (renomear quebraria `TextFonts.cs`). |
| 12 | Logs / exceções / 1ª execução | `DispatcherUnhandledException` loga e segue rodando; `Log` nunca lança; `SettingsV1.Normalize()` blinda settings corrompidos (futuro). Primeira execução cria as pastas sozinha. |

## 2. Arquitetura de distribuição escolhida

- **Self-contained win-x64, single-file, sem trimming.** Compatibilidade > tamanho:
  WPF + reflection (XAML) + P/Invoke não toleram trimming agressivo.
- **Sem dependência**: sem .NET instalado, sem VS, sem arquivos da máquina de dev.
- **Offline-first preservado**: zero servidor/banco/auth/internet (só o `restore` inicial do NuGet, na máquina de build).
- **Startup/memória**: single-file extrai para memória no 1º start (custo de ~1–2 s frios aceito); orçamentos de runtime (warm ≤ 500 ms, idle ≤ 80 MB) inalterados — ver skill `epic-pencil-guardrails`.
- **Tamanho atual**: ~130 MB (runtime .NET 9 embutido). Reduzir é prioridade 5 — só depois de instalador estável (candidatos futuros: `PublishReadyToRun`, corte de ICU, nunca trimming de WPF sem validação total).

## 3. Estrutura dos artefatos

```text
dist/
  win-x64/
    EpicPencil-<versão>/          # saída do publish (o que vai no instalador)
      EpicPencil.Shell.exe        # executável único (~130 MB)
      Fonts/OFL.txt               # licença da fonte (Content)
  EpicPencil-win-x64-<versão>.zip # pasta acima zipada (compartilhamento manual)
installer/
  inno/
    setup.iss                     # fonte do instalador (Inno Setup 6+)
  output/
    EpicPencil-Setup-<versão>.exe # gerado por `iscc` (ignorado pelo git)
```

`dist/` é ignorado pelo git (`.gitignore`); o instalador lê de `dist/` (nunca de `bin/`).

## 4. Versionamento (Major.Minor.Patch)

- Fonte única: `<Version>` em `Directory.Build.props` (+ `AssemblyVersion`,
  `FileVersion`, `InformationalVersion` travados, sem sufixo de commit → build reproduzível).
- `Log.Init()` já imprime `Assembly.GetEntryAssembly().Version` no log — a versão
  aparece automaticamente no exe (Propriedades → Detalhes), no nome do instalador
  (`EpicPencil-Setup-<versão>.exe`) e na 1ª linha de cada log.
- `installer/inno/setup.iss` tem `#define AppVersion` que **precisa** igualar o props;
  `scripts/publish-win-x64.ps1` falha se divergirem.
- Nova versão: `0.1.0` → `0.2.0` (feature) / `0.1.1` (fix); ver §9.

## 5. Comportamento de instalação (base Inno Setup)

- `installer/inno/setup.iss` (Inno 6+, `iscc installer\inno\setup.iss` após o publish).
- Instala em `{autopf}\Epic Pencil` (Program Files, requer admin), atalho no Menu
  Iniciar + opcional na área de trabalho (task desmarcada por padrão), entrada de
  desinstalação no Painel de Controle, opção de iniciar ao final (checked).
- **Atualização** = rodar o novo `EpicPencil-Setup-<nova>.exe` por cima (mesmo `AppId`
  + `ignoreversion` nos arquivos): binários trocados, dados preservados.
- **Desinstalação** remove só `{app}`. `%LocalAppData%\EpicPencil` (logs, futuros
  settings) é **preservado** de propósito; instalação limpa = desinstalar + apagar
  essa pasta manualmente.
- Sem autostart/startup entry (o app não tem tray/serviço; nada roda em background).
- Pendência honesta antes do instalador final: ícone próprio (`assets\epic-pencil.ico`
  + linha `SetupIconFile` no `.iss`) e compilação smoke-test com Inno (não instalado
  nesta máquina — ver §8).

## 6. Configurações e logs

| Dado | Local | Ciclo de vida |
|---|---|---|
| Logs de diagnóstico | `%LocalAppData%\EpicPencil\Logs\yyyyMMdd.log` | Criado sozinho; preservado em update/uninstall |
| Settings do usuário | (futuro) `%LocalAppData%\EpicPencil\settings.json` via `SettingsV1.Normalize()` | Mesmo contrato: update preserva, uninstall preserva, "limpa" = apagar a pasta |
| PNG exportado | Onde o usuário escolher no diálogo | Fora do escopo do instalador |

## 7. Requisitos mínimos e arquitetura

- **SO**: Windows 10 1809 (build 17763) x64 ou Windows 11 x64 (mínimo do .NET 9 WPF; refletido em `MinVersion` no `.iss`).
- **Arquitetura**: x64 (`ArchitecturesAllowed=x64compatible`). ARM64 é pós-MVP (exigiria publish `win-arm64` + validação de P/Invoke/GDI — anotado, não iniciado).
- **Na máquina de destino**: nada — sem .NET, sem VS, sem WebView, sem drivers. Conta de usuário padrão basta (admin só para instalar em Program Files).
- **Na máquina de build**: .NET SDK 9 (`global.json`), Windows x64, acesso ao nuget.org só no 1º `restore`.

## 8. Como testar em máquina limpa (sem .NET)

Não há máquina limpa disponível neste ambiente — o checklist abaixo é o que precisa
ser validado manualmente (VM Win10 1809+/Win11 sem SDK, snapshot antes):

1. `dotnet build -c Release` ✓ (automatizado no script)
2. `dotnet test -c Release` ✓ (automatizado; 57 testes verdes em 2026-09-15)
3. Self-tests: `EpicPencil.Shell.exe --selftest` → `SHELL SELFTEST OK`, exit 0 ✓ (validado no exe publicado desta máquina)
4. `dotnet publish -r win-x64 --self-contained` ✓ (automatizado no script)
5. Copiar `dist\win-x64\EpicPencil-<v>\` para a VM **sem .NET** e abrir o exe — deve abrir sem erro de runtime ausente
6. Startup: overlay + toolbar aparecem em < ~2 s frios, log criado em `%LocalAppData%\EpicPencil\Logs\`
7. Toolbar: recolher/expandir, trocar ferramenta, slider, swatches
8. Desenho: caneta/lápis/marca-texto riscam sobre o desktop
9. Seleção (V): clicar seleciona texto/captura; área vazia desseleciona
10. Texto (T): caixa abre, Enter commita, Escape cancela
11. Captura: marquee cria ScreenObject; Ctrl+C cola no Paint; Ctrl+S salva PNG válido
12. Clipboard: Ctrl+V no Paint/Word mostra a imagem com a tinta composta
13. Save: diálogo padrão, `OverwritePrompt`, PNG abre normal
14. Undo/Redo: Ctrl+Z / Ctrl+Y em desenho, borracha, texto, captura, formas
15. Hotkeys locais: P/B/H/L/S/R/O/V/E/T/D1-D3/C/Delete/F9/PgUp/Esc (foco na toolbar)
16. Fechamento: fechar toolbar **ou** overlay encerra o processo (sem órfãs no Task Manager)
17. Multimonitor (2 telas físicas): 1 overlay por work area, `mons=2` no status, tinta atravessa o gesto sem sumir
18. DPI misto + coords negativas: secundário à esquerda/100%+150% — stroke commita na posição certa (cobertura automatizada em `RunMonitors`; conferência visual na VM)
19. Instalador: `EpicPencil-Setup-<v>.exe` instala em Program Files, atalhos funcionam, desinstala sem apagar `%LocalAppData%\EpicPencil`
20. Update: instalar `<v+1>` por cima preserva logs/settings; versão no Painel de Controle e no log batem

## 9. Como gerar uma nova versão (runbook)

```powershell
# 1. Bump de versão (2 lugares, script valida a sincronia):
#    - Directory.Build.props  -> <Version>0.2.0</...>
#    - installer/inno/setup.iss -> #define AppVersion "0.2.0"
# 2. Validar + publicar:
powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1
# 3. Teste manual na máquina limpa (§8, itens 5–20).
# 4. Instalador (requer Inno Setup 6+):
iscc installer\inno\setup.iss
# 5. Entregar: installer\output\EpicPencil-Setup-<versão>.exe
#    (o .zip em dist\ é alternativa sem instalador)
```

Script: `scripts/publish-win-x64.ps1` (flags `-SkipTests`, `-NoZip`).
Comando equivalente manual está no próprio script (passo 5).
