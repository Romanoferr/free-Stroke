// Comandos de texto: mover, atualizar (conteúdo/tamanho) e apagar — mesmo
// padrão dos comandos de captura (MoveScreenCommand/RemoveScreenCommand).
// Sem sistema paralelo: o UndoStack existente guarda Do/Undo com teto duplo.

using EpicPencil.Core;

namespace EpicPencil.Core;

public sealed class AddTextCommand : IDocCommand
{
    private readonly TextObject _text;
    public AddTextCommand(TextObject text) => _text = text;
    public int RetainedPoints => _text.Content.Length;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<TextObject> AffectedTexts => [_text];
    public void Do(Document doc) => doc.AddText(_text);
    public void Undo(Document doc) => doc.RemoveText(_text);
}

public sealed class MoveTextCommand : IDocCommand
{
    private readonly TextObject _text;
    private readonly float _oldX, _oldY, _newX, _newY;
    public MoveTextCommand(TextObject text, float oldX, float oldY, float newX, float newY)
    {
        _text = text;
        _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }
    public int RetainedPoints => 0; // objeto continua no Document; nada retido a mais
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<TextObject> AffectedTexts => [_text];
    public void Do(Document doc) { _text.X = _newX; _text.Y = _newY; }
    public void Undo(Document doc) { _text.X = _oldX; _text.Y = _oldY; }
}

// Reedição e troca de tamanho: guarda o antes/depois completo (conteúdo,
// tamanho e métricas). Um comando por commit — um Ctrl+Z desfaz tudo.
public sealed class UpdateTextCommand : IDocCommand
{
    private readonly TextObject _text;
    private readonly string _oldContent, _newContent;
    private readonly float _oldSize, _newSize, _oldW, _newW, _oldH, _newH;
    public UpdateTextCommand(TextObject text, string newContent,
        float newFontSizePx, float newWidthPx, float newHeightPx)
    {
        _text = text;
        _oldContent = text.Content;
        _oldSize = text.FontSizePx;
        _oldW = text.WidthPx;
        _oldH = text.HeightPx;
        _newContent = newContent;
        _newSize = newFontSizePx;
        _newW = newWidthPx;
        _newH = newHeightPx;
    }
    public int RetainedPoints => _newContent.Length;
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<TextObject> AffectedTexts => [_text];
    public void Do(Document doc) => Apply(_newContent, _newSize, _newW, _newH);
    public void Undo(Document doc) => Apply(_oldContent, _oldSize, _oldW, _oldH);
    private void Apply(string content, float size, float w, float h)
    {
        _text.Content = content;
        _text.FontSizePx = size;
        _text.WidthPx = w;
        _text.HeightPx = h;
    }
}

public sealed class EraseTextsCommand : IDocCommand
{
    private readonly List<(TextObject Text, int Index)> _removed = new();
    public EraseTextsCommand(IReadOnlyList<TextObject> removed, Document doc)
    {
        var all = doc.Texts;
        foreach (var t in removed)
            _removed.Add((t, IndexOf(all, t)));
    }
    public int RetainedPoints
    {
        get
        {
            int n = 0;
            foreach (var r in _removed) n += r.Text.Content.Length;
            return n;
        }
    }
    public IReadOnlyList<Stroke> Affected => Array.Empty<Stroke>();
    public IReadOnlyList<TextObject> AffectedTexts => _removed.Select(r => r.Text).ToList();
    public void Do(Document doc)
    {
        foreach (var r in _removed) doc.RemoveText(r.Text);
    }
    public void Undo(Document doc)
    {
        var ordered = new List<(TextObject Text, int Index)>(_removed);
        ordered.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var r in ordered)
            doc.InsertText(r.Text, Math.Min(r.Index, doc.TextCount));
    }
    private static int IndexOf(IReadOnlyList<TextObject> all, TextObject t)
    {
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], t)) return i;
        return all.Count;
    }
}
