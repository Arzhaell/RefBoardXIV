using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace RefBoardXIV;

/// <summary>Affiche et masque la fenêtre du tableau : à ta demande, et automatiquement selon ce qui se passe en jeu.</summary>
internal sealed class BoardVisibility : IDisposable
{
    private readonly Configuration config;
    private readonly Window window;
    private readonly AutoHideState autoHide = new();

    public BoardVisibility(Configuration config, Window window)
    {
        this.config = config;
        this.window = window;
        window.IsOpen = config.BoardOpen;
        Plugin.Framework.Update += OnUpdate;
    }

    public bool IsOpen => window.IsOpen;

    /// <summary>Le tableau est masqué à cause d'une situation du jeu (cinématique…).</summary>
    public bool IsAutoHidden => autoHide.HidByPlugin;

    public void Toggle() => SetOpen(!window.IsOpen);

    /// <summary>Affiche ou masque le tableau à ta demande (le choix est retenu d'une session à l'autre).</summary>
    public void SetOpen(bool open)
    {
        window.IsOpen = open;
        autoHide.Forget();
        if (config.BoardOpen == open)
            return;
        config.BoardOpen = open;
        config.Save();
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        switch (autoHide.Update(AutoHideState.ShouldHide(config.AutoHide, CurrentSituations()), window.IsOpen))
        {
            case AutoHideAction.Hide:
                window.IsOpen = false;
                break;
            case AutoHideAction.Show:
                window.IsOpen = true;
                break;
        }
    }

    private static HideSituation CurrentSituations()
    {
        var condition = Plugin.Condition;
        var gpose = Plugin.ClientState.IsGPosing;
        var situations = HideSituation.None;

        // La gpose peut aussi lever les marqueurs de cinématique : elle a sa propre case.
        if (!gpose && (condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene]
            || condition[ConditionFlag.WatchingCutscene78]))
            situations |= HideSituation.Cutscene;
        if (gpose)
            situations |= HideSituation.GPose;
        if (condition[ConditionFlag.InCombat])
            situations |= HideSituation.Combat;
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            situations |= HideSituation.Loading;
        if (Plugin.GameGui.GameUiHidden)
            situations |= HideSituation.GameUiHidden;
        return situations;
    }
}
