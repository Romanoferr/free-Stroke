// Comando de texto: criação como operação de undo (mesmo padrão dos strokes).
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
