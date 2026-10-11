using System;

namespace RefBoardXIV;

/// <summary>Situations du jeu qui peuvent masquer le tableau automatiquement.</summary>
[Flags]
public enum HideSituation
{
    None = 0,
    Cutscene = 1,
    GPose = 2,
    Combat = 4,
    Loading = 8,
    GameUiHidden = 16,
}

internal enum AutoHideAction
{
    None,
    Hide,
    Show,
}

/// <summary>
/// Masquage automatique : le tableau est masqué quand une situation cochée commence, et revient quand elle finit,
/// seulement si c'est le plugin qui l'avait masqué (on ne fait pas réapparaître un tableau que tu avais toi-même masqué).
/// </summary>
internal sealed class AutoHideState
{
    private bool active;

    /// <summary>Vrai tant que le tableau est masqué à cause d'une situation du jeu.</summary>
    public bool HidByPlugin { get; private set; }

    public static bool ShouldHide(HideSituation enabled, HideSituation current) => (enabled & current) != HideSituation.None;

    /// <summary>À appeler à chaque image. <paramref name="shown"/> : le tableau est à l'écran.</summary>
    public AutoHideAction Update(bool shouldHide, bool shown)
    {
        if (shouldHide == active)
            return AutoHideAction.None;
        active = shouldHide;

        if (shouldHide)
        {
            if (!shown)
                return AutoHideAction.None;
            HidByPlugin = true;
            return AutoHideAction.Hide;
        }

        if (!HidByPlugin)
            return AutoHideAction.None;
        HidByPlugin = false;
        return AutoHideAction.Show;
    }

    /// <summary>Affiché ou masqué à la main : la fin de la situation ne le fera pas revenir.</summary>
    public void Forget() => HidByPlugin = false;
}
