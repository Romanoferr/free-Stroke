// Schema de settings v1: valores com clamp na carga (arquivo corrompido ou
// editado à mão nunca pode travar o startup — defaults vencem).
// Persistência física (arquivo atômico) mora em EpicPencil.Windows, não aqui.

namespace EpicPencil.Core;

public sealed class ToolPreset
{
    public Rgba Color { get; set; } = new(255, 0, 0);
    public float WidthDip { get; set; } = 4f;
}

public sealed class SettingsV1
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;

    public ToolKind LastTool { get; set; } = ToolKind.Pen;
    public Dictionary<ToolKind, ToolPreset> Presets { get; set; } = new()
    {
        [ToolKind.Pen] = new ToolPreset { Color = new Rgba(255, 0, 0), WidthDip = 4f },
        [ToolKind.Pencil] = new ToolPreset { Color = new Rgba(30, 30, 30), WidthDip = 2f },
        [ToolKind.Highlighter] = new ToolPreset { Color = new Rgba(255, 235, 59), WidthDip = 18f },
        [ToolKind.Line] = new ToolPreset { Color = new Rgba(255, 0, 0), WidthDip = 4f },
        [ToolKind.Arrow] = new ToolPreset { Color = new Rgba(255, 0, 0), WidthDip = 5f },
    };

    public string ToggleDrawHotkey { get; set; } = "Alt+Shift+D";
    public string HideInkHotkey { get; set; } = "Alt+Shift+H";
    public string PanicHotkey { get; set; } = "Ctrl+Alt+Shift+X";

    public static SettingsV1 WithDefaults() => new();

    public void Normalize()
    {
        Version = CurrentVersion;
        foreach (var kv in Presets)
        {
            kv.Value.WidthDip = Math.Clamp(kv.Value.WidthDip, 1f, 64f);
            if (kv.Value.Color.A == 0) kv.Value.Color = kv.Value.Color with { A = 255 };
        }
        if (string.IsNullOrWhiteSpace(ToggleDrawHotkey)) ToggleDrawHotkey = "Alt+Shift+D";
        if (string.IsNullOrWhiteSpace(HideInkHotkey)) HideInkHotkey = "Alt+Shift+H";
        if (string.IsNullOrWhiteSpace(PanicHotkey)) PanicHotkey = "Ctrl+Alt+Shift+X";
    }
}
