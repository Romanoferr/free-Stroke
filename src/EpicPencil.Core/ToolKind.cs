// Core puro: sem System.Windows, sem DllImport, sem WPF.
// Alvo net9.0 (neutro) de propósito: se alguém referenciar UI/Win32 aqui, quebra em review.

namespace EpicPencil.Core;

/// <summary>Ferramentas do MVP. Pencil existe como preset de Pen (mesma geometria).</summary>
public enum ToolKind
{
    Pen,
    Pencil,
    Highlighter,
    Line,
    Arrow,
    Rectangle, // contorno paramétrico (RectangleObject, não stroke assado)
    Circle, // elipse inscrita no bbox (CircleObject, não stroke assado)
    EraserStroke,
    Select, // seleção genérica de OBJETO (texto/captura/futuros) + mover + marquee de REGIÃO p/ captura; nunca produz stroke
    Text // texto: clique abre edição; commit cria TextObject (não produz stroke)
}
