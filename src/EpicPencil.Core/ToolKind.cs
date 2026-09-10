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
    EraserStroke,
    Select // seleção de região (captura) + mover captura; não produz stroke
}
