// Roteador ÚNICO de hotkeys locais (vive na ToolbarWindow.PreviewKeyDown — o
// overlay nunca tem foco, então as teclas moram na toolbar, expandida ou recolhida).
// REQ3: combinações com modificador são ações DISTINTAS de teclas isoladas
// (C ≠ Ctrl+C, S ≠ Ctrl+S). Resolve retorna UMA ação (sem eventos duplicados);
// None = ignora a tecla. Local apenas: sem RegisterHotKey, sem hook global
// (veto de arquitetura: Ctrl+letra global é proibido).

using System.Windows.Input;

namespace EpicPencil.Shell;

internal enum HotkeyAction
{
    None,
    ToolPen,
    ToolPencil,
    ToolHighlighter,
    ToolLine,
    ToolArrow,
    ToolRectangle,
    ToolCircle,
    ToolSelect,
    ToolEraser,
    ToolText,
    WidthPreset0,
    WidthPreset1,
    WidthPreset2,
    Clear,
    DeleteScreen, // Delete: remove o OBJETO selecionado (texto ou captura, REQ5)
    ToggleInkMode,
    HideInk,
    Undo,
    Redo,
    CopyCapture,
    SaveCapture,
}

internal static class HotkeyRouter
{
    public static HotkeyAction Resolve(Key key, ModifierKeys modifiers)
    {
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        // Combinações Ctrl+tecla primeiro: nunca caem no comportamento da tecla isolada.
        if (ctrl && key == Key.C) return HotkeyAction.CopyCapture;
        if (ctrl && key == Key.S) return HotkeyAction.SaveCapture;
        if (ctrl && key == Key.Z) return HotkeyAction.Undo;
        if (ctrl && key == Key.Y) return HotkeyAction.Redo;
        // Qualquer outro modificador (Ctrl/Alt/Shift/Win) descaracteriza o
        // atalho isolado: Ctrl+P, Shift+S, Alt+C etc. não trocam ferramenta.
        if (modifiers != ModifierKeys.None) return HotkeyAction.None;
        return key switch
        {
            Key.P => HotkeyAction.ToolPen,
            Key.B => HotkeyAction.ToolPencil,
            Key.H => HotkeyAction.ToolHighlighter,
            Key.L => HotkeyAction.ToolLine,
            Key.S => HotkeyAction.ToolArrow,
            Key.R => HotkeyAction.ToolRectangle,
            Key.O => HotkeyAction.ToolCircle,
            Key.V => HotkeyAction.ToolSelect,
            Key.E => HotkeyAction.ToolEraser,
            Key.T => HotkeyAction.ToolText,
            Key.D1 => HotkeyAction.WidthPreset0,
            Key.D2 => HotkeyAction.WidthPreset1,
            Key.D3 => HotkeyAction.WidthPreset2,
            Key.C => HotkeyAction.Clear,
            Key.Delete => HotkeyAction.DeleteScreen,
            Key.F9 => HotkeyAction.ToggleInkMode,
            Key.PageUp => HotkeyAction.ToggleInkMode,
            Key.OemQuotes => HotkeyAction.ToggleInkMode,
            Key.Escape => HotkeyAction.HideInk,
            _ => HotkeyAction.None,
        };
    }
}
