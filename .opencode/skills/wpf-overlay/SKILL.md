---
name: wpf-overlay
description: Win32 overlay recipe for Epic Pencil (transparent fullscreen, click-through, no-focus, topmost). Use when touching OverlayWindow, OverlayBehavior, Native interop, multi-monitor, DPI, fullscreen, focus/activation, or hotkey code.
---

# WPF Overlay Recipe (validated by spike S1)

## Base window (all required, none optional)

- XAML: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`, `ResizeMode=NoResize`, `ShowActivated=False`.
- Fullscreen set manually in code (`Left/Top/Width/Height` from screen bounds). WPF throws if `ShowActivated=False` combines with `WindowState=Maximized`.
- One window per monitor at its exact `rcMonitor` (never one spanning window — breaks mixed DPI).

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
