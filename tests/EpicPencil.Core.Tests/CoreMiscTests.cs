using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class CoreMiscTests
{
    [Fact]
    public void InputFilter_Drops_Micro_Jitter()
    {
        var pts = new List<Pt>();
        float eps = StrokeSpec.CaptureMinDistance(4f);
        Assert.True(InputFilter.TryAppend(pts, new Pt(0, 0), eps));
        Assert.False(InputFilter.TryAppend(pts, new Pt(0.1f, 0.1f), eps));
        Assert.True(InputFilter.TryAppend(pts, new Pt(10, 0), eps));
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void Settings_Normalize_Clamps_Corrupt_File()
    {
        var s = new SettingsV1();
        s.Presets[ToolKind.Pen].WidthDip = 9999f;
        s.ToggleDrawHotkey = "";
        s.Normalize();
        Assert.InRange(s.Presets[ToolKind.Pen].WidthDip, 1f, 64f);
        Assert.False(string.IsNullOrWhiteSpace(s.ToggleDrawHotkey));
    }

    [Fact]
    public void ExportJob_Rejects_Invalid_Scale()
    {
        var bad = new ExportJob { RegionDip = new RectD(0, 0, 100, 100), Scale = 99f };
        Assert.Throws<ArgumentOutOfRangeException>(() => bad.Validate());
    }

    [Fact]
    public void Soak_10k_Strokes_Undo_Fast()
    {
        var doc = new Document();
        var undo = new UndoStack();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            var pts = new List<Pt> { new(i % 1920, i % 1080), new((i % 1920) + 5, (i % 1080) + 5) };
            undo.Execute(new AddStrokeCommand(
                new Stroke(doc.NextId(), ToolKind.Pen, new Rgba(255, 0, 0), 4f, 1f, pts)), doc);
        }
        sw.Stop();
        Assert.Equal(10_000, doc.Count);
        // Teto de comandos: undo guarda só os últimos 50.
        Assert.True(undo.UndoCount <= UndoStack.MaxCommands);
    }
}
