---
name: wpf-overlay
description: Win32 overlay recipe for Epic Pencil (transparent fullscreen, click-through, no-focus, topmost). Use when touching OverlayWindow, OverlayBehavior, Native interop, multi-monitor, DPI, fullscreen, focus/activation, or hotkey code.
---

# WPF Overlay Recipe (validated by spike S1)

## Base window (all required, none optional)

- XAML: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`, `ResizeMode=NoResize`, `ShowActivated=False`.
- Fullscreen set manually in code (`Left/Top/Width/Height` from screen bounds). WPF throws if `ShowActivated=False` combines with `WindowState=Maximized`.
- One window per monitor at its exact `rcMonitor` (never one spanning window — breaks mixed DPI).

## Interaction regions (REQ1–REQ3: draw area vs interaction area)

- **Draw area = overlay ∩ work area.** Size the overlay to `SystemParameters.WorkArea` (WPF) / per-monitor `rcWork` (Win32), NEVER full `rcMonitor`: the taskbar/appbars are shell-owned interaction areas and must keep working in draw mode. Annotating over the taskbar is explicitly out of scope.
- **Interaction areas = real windows above the overlay.** Toolbar is a separate `Topmost` window with `Owner = overlay` (WPF requires the owner SHOWN before assigning `Owner`, else `InvalidOperationException`) — OS-guaranteed above, draggable by its native titlebar to any position; its clicks never reach the ink layer by construction (separate HWND on top).
- No `HTTRANSPARENT` hole-punching needed while interactive surfaces are separate windows. If an interactive control ever lives INSIDE the overlay window, punch holes via `WM_NCHITTEST → HTTRANSPARENT` per-rect instead of toggling `WS_EX_TRANSPARENT`.
- Alpha floor: full-bleed `#01000000` background behind the ink surface (see previous section).

## Win32 styles (`OverlayBehavior`)

- Base: `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, clear `WS_EX_TRANSPARENT` (start clickable).
- Click-through toggle: flip `WS_EX_TRANSPARENT` via `SetWindowLongPtr` **then** `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|NOSIZE|NOACTIVATE|FRAMECHANGED)`. Without `FRAMECHANGED` the bit changes but hit-testing does not — the classic trap.
- Focus: `HwndSource` hook returns `MA_NOACTIVATE` for `WM_MOUSEACTIVATE`. `WS_EX_TRANSPARENT` alone does NOT prevent activation.
- Topmost: reassert with `NOACTIVATE|NOREDRAW` on events (`WinEventHook FOREGROUND`, `DisplayChange`, `DwmCompositionChanged`) — never a timer.
- Self-test acceptance: 20 toggles, p95 < 50 ms, `GetForegroundWindow() != hwnd` throughout, `RenderCapability.Tier >> 16 >= 1` (tier 0 = software fallback, escalate).

## Known limitations (document, don't "fix" in MVP)

- UIPI: non-elevated overlay never covers elevated apps. Never request admin to work around it.
- Exclusive-fullscreen (DXGI flip) can hide any overlay — limitation, future capture-mode is opt-in.
- Teams/Meet/Zoom may not capture the overlay (window vs composition capture) — must be characterized per tool before release notes.
- Self-test proves styles/focus/timing only. Pixel click-through ("click lands in app below") and mixed-DPI positioning require manual runs: `dotnet run --project prototypes/S1.Overlay` (+ `taskkill /IM S1.Overlay.exe /F` to exit click-through mode, which is intentionally unclickable).

## Click diagnostics (ToolbarWindow: Flash / Who / Front)

When clicks never reach the overlay (`janela PreviewMouseDown/StylusDown` absent from log despite draw mode + `transparent=False`):

1. **Flash** (`OverlayWindow.FlashTest`): paints fullscreen red 400 ms. Red seen = window present and on top (problem is input routing). No red = window absent/covered (z-order/visibility problem).
2. **Who** (`OverlayBehavior.DescribePointOwner` via `WindowFromPoint`): identifies the HWND under the cursor with process name. `nosso=True` = clicks reach us (routing/promotion bug). Another proc = the click thief (log its name).
3. **Front** (`ReassertTopmost` + `Describe` readback): if clicks work after this, root cause was z-order loss — then find who stole it (foreground-change hook) instead of polling.
4. Overlay logs bounds/monitors/ex-style/tier at startup; every `ApplyState` logs mode + click-through readback. `input begin src=mouse|stylus` tells which funnel fired (dedup via `InputDedup`, 80 ms window).

## Empty-canvas hit-testing gotchas (learned debugging real misses)

- A raw `FrameworkElement` (no `Background`) can miss visual hit-testing on empty areas. Defense in depth, in order: (1) `HitTestCore` override returning self, (2) `_hit`: full-bleed `Transparent`-brush rect visual (invisible but hittable) refreshed in `OnRenderSizeChanged`.
- CAUTION: the simple `VisualTreeHelper.HitTest(visual, point)` overload calls `HitTestCore` directly and can PASS while real clicks still miss. The callback-based `HitTest` overload is closer to the `WM_NCHITTEST` path — but even it can pass headless while the live window misses. Ground truth is live-only: `ProbeHitTest` (`SendMessage WM_NCHITTEST`, expect `HTCLIENT=1`; `HTTRANSPARENT=-1` means pass-through) + `DescribeZOrder` (real rects via `GetWindowRect`, `vis`/`dis` flags, `>>OVERLAY<<` marker). Never declare hit-testing fixed without the live probe reading `HTCLIENT`.
- **Alpha-zero drill-through**: on `UpdateLayeredWindow` (`AllowsTransparency`) windows the OS compositor hit-test consults the ALPHA BITMAP, not just `WM_NCHITTEST`/`WS_EX_TRANSPARENT`. Signature: z-order + `vis`/`dis` + rect all correct, direct `NCHITTEST=HTCLIENT`, yet `WindowFromPoint` returns the app below. Fix: alpha floor — full-bleed `#01000000` background (invisible, <1 LSB) so the bitmap is solid to the compositor. `WS_EX_TRANSPARENT` (interact mode) still passes through regardless of alpha.
