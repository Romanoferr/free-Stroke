---
name: epic-pencil-guardrails
description: Guardrails do projeto Epic Pencil (overlay de anotação Windows). Use when editing any code under src/, tests/ or prototypes/ of epic-pencil, or when planning MVP scope, performance budgets, hotkeys, rendering, or undo.
---

# Epic Pencil Guardrails

Enforce these on every change. If a request violates a veto, push back and cite the rule.

## Hard vetos (from ARCHITECTURE-REVIEW.md)

- No Electron, no WebView, no database, no backend, no auth, no cloud, no screen capture for drawing.
- No global `Ctrl+Z` (or any single-modifier/`Ctrl+letter` global hotkey). Globals allowed only: `Alt+Shift+D`, `Alt+Shift+H`, panic `Ctrl+Alt+Shift+X`, all via `RegisterHotKey` — never `WH_KEYBOARD_LL` hooks.
- No zoom/pan of the ink layer in the MVP. `Camera` stays identity.
- No `InkCanvas` in the interactive path. Interactive renderer is `DrawingVisual` only (`WpfStrokeRenderer`); Skia is export-only and lazy-loaded.
- No bitmap snapshots for undo. Vector `IDocCommand` stack only, dual cap (50 cmds / 250k points).
- No z-order polling loops. Topmost reassert is event-driven only (protects ~0% idle CPU).

## Layer rules

- `EpicPencil.Core` (net9.0, platform-neutral): no `System.Windows`, no `DllImport`, no WPF types — not even in comments of new code. Verify with grep for `System\.Windows|DllImport|Presentation` (only pre-existing comment hits allowed).
- `EpicPencil.Windows` (net9.0-windows): raw HWND logic only (`IntPtr` in/out). No WPF assemblies; the `HwndSource` hook is wired in Shell via `OverlayBehavior.TryHandleMouseActivate`.
- `EpicPencil.Shell`: all WPF/XAML lives here. State flows through `AppState` events (`StrokeAdded`/`StrokesRemoved`/`StateChanged`) — no duplicated state in windows.
- `IDocCommand.Affected` must stay accurate: Shell syncs visuals from it, never rebuild-all.

## Increment protocol (every code change)

1. Implement small. 2. `dotnet build EpicPencil.sln` (0 warnings goal). 3. `dotnet test` (Core 16+ tests green). 4. Run affected self-test (`S1.Overlay --selftest`, `EpicPencil.Shell --selftest`). 5. Report: files changed, validation output, risks, next step.

## Perf budgets (conditional on refresh rate)

- Warm startup ≤ 500 ms, idle RAM ≤ 80 MB private, idle CPU < 1% (5-min ETW average).
- Processing (pointer → visual rebuilt) p50 < 3 ms; end-to-end p50 ≤ 25 ms @60Hz, ≤ 16 ms @120Hz+.
- Always state hardware (mouse Hz, monitor Hz, 1080p/4K) next to any latency number.

## MVP scope gate

MUST loop only: open → draw → erase → undo → hide → interact → draw again.
Marker/line/arrow, PNG export, tray, global hotkeys, multi-monitor/DPI, settings file, single-instance: staged, in that priority order. Anything else is POST-MVP — say so explicitly.
