// Resolução da família de texto: Space Mono embutida (Fonts/*.ttf, licença
// OFL em Fonts/OFL.txt) com fallback determinístico p/ Consolas (monospace do
// sistema) se o recurso sumir. Edição e finalizado usam a mesma família.

using System.Windows.Media;

namespace EpicPencil.Shell;

internal static class TextFonts
{
    public const string PreferredFamily = "Space Mono";

    private static FontFamily? _cached;

    public static FontFamily Family => _cached ??= Resolve();

    // Família efetiva p/ um nome do modelo (futuro: outras fontes por objeto).
    public static FontFamily ResolveFamily(string familyName) =>
        familyName.Equals(PreferredFamily, StringComparison.OrdinalIgnoreCase)
            ? Family
            : new FontFamily(familyName);

    private static FontFamily Resolve()
    {
        try
        {
            var baseUri = new Uri(
                "pack://application:,,,/EpicPencil.Shell;component/Fonts/", UriKind.Absolute);
            foreach (var f in Fonts.GetFontFamilies(baseUri))
                foreach (var name in f.FamilyNames.Values)
                    if (name.Equals(PreferredFamily, StringComparison.OrdinalIgnoreCase))
                        return new FontFamily(baseUri, "./#Space Mono");
        }
        catch (Exception ex)
        {
            Windows.Log.Warn($"Space Mono embutida indisponível ({ex.Message}); usando Consolas");
        }
        return new FontFamily("Consolas");
    }
}
