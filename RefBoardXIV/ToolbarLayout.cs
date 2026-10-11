using System;
using System.Collections.Generic;
using System.Linq;

namespace RefBoardXIV;

/// <summary>
/// Mise en page de la barre du tableau sur une seule ligne, quelle que soit la largeur de la fenêtre : la liste des
/// tableaux rétrécit d'abord, puis les boutons qui ne tiennent plus (en partant de la droite) passent dans un menu « … ».
/// </summary>
internal static class ToolbarLayout
{
    /// <param name="available">Largeur de la ligne.</param>
    /// <param name="buttons">Largeur de chaque bouton, espace avant lui compris, dans l'ordre de la barre.</param>
    /// <param name="moreButton">Largeur du bouton « … », espace avant lui compris.</param>
    /// <returns>Largeur de la liste des tableaux, et nombre de boutons affichés (les premiers).</returns>
    public static (float ComboWidth, int Shown) Fit(float available, IReadOnlyList<float> buttons, float moreButton, float comboMin, float comboMax)
    {
        var all = buttons.Sum();
        if (available - all >= comboMin)
            return (Math.Min(comboMax, available - all), buttons.Count);

        var shown = 0;
        var used = comboMin + moreButton;
        while (shown < buttons.Count && used + buttons[shown] <= available)
            used += buttons[shown++];

        // La place qui reste (trop petite pour un bouton de plus) revient à la liste des tableaux.
        return (Math.Clamp(available - (used - comboMin), comboMin, comboMax), shown);
    }
}
