using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace RefBoardXIV;

/// <summary>
/// Affiche et masque la fenêtre du tableau : à ta demande, et automatiquement selon ce qui se passe en jeu. Il ne
/// s'ouvre jamais tout seul (fermé au lancement du jeu) et ne s'affiche que connecté à un personnage.
/// </summary>
internal sealed class BoardVisibility : IDisposable
{
    private readonly Configuration config;
    private readonly Window window;
    private readonly AutoHideState autoHide = new();

    public BoardVisibility(Configuration config, Window window)
    {
        this.config = config;
        this.window = window;
        window.IsOpen = false;
        Plugin.Framework.Update += OnUpdate;
    }

    public bool IsOpen => window.IsOpen;

    /// <summary>Le tableau est masqué à cause d'une situation du jeu (cinématique…).</summary>
    public bool IsAutoHidden => autoHide.HidByPlugin;

    /// <summary>Connecté à un personnage : le tableau peut s'afficher.</summary>
    public static bool CanShow => Plugin.ClientState.IsLoggedIn;

    public void Toggle() => SetOpen(!window.IsOpen);

    /// <summary>Affiche ou masque le tableau à ta demande.</summary>
    public void SetOpen(bool open)
    {
        autoHide.Forget();
        if (open && !CanShow)
        {
            Plugin.Notifications.AddNotification(new Notification
            {
                Title = "RefBoardXIV",
                Content = Loc.T("Log in to a character to show the board.", "Connecte-toi à un personnage pour afficher le tableau."),
                Type = NotificationType.Info,
            });
            return;
        }

        window.IsOpen = open;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        // Écran titre, choix du personnage, déconnexion : le tableau se ferme, et attend que tu le rouvres.
        if (!CanShow)
        {
            if (window.IsOpen || autoHide.HidByPlugin)
            {
                autoHide.Forget();
                window.IsOpen = false;
            }

            return;
        }

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
