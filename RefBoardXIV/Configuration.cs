using System;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;

namespace RefBoardXIV;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public PluginLanguage Language { get; set; } = PluginLanguage.Auto;

    /// <summary>Le tableau est affiché (ce que tu as choisi ; le masquage automatique n'y touche pas).</summary>
    public bool BoardOpen { get; set; } = true;

    /// <summary>Tableau affiché dans la fenêtre.</summary>
    public Guid CurrentBoard { get; set; }

    /// <summary>Tableau verrouillé : il ne reste que les images de la fenêtre, et les clics passent au jeu.</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Hauteur de la barre de titre et des boutons du tableau (pixels), mesurée tableau déverrouillé : verrouillé, la zone
    /// des images reste à cette distance du haut de la fenêtre. 0 tant qu'elle n'a pas été mesurée.
    /// </summary>
    public float CanvasTop { get; set; }

    /// <summary>Le cadre de la zone des images reste affiché une fois le tableau verrouillé.</summary>
    public bool FrameWhenLocked { get; set; }

    /// <summary>Opacité des images, de 0.1 à 1.</summary>
    public float ImageOpacity { get; set; } = 1f;

    // Raccourcis clavier. Un raccourci effacé est gardé avec « aucune touche », pour ne pas revenir à celui par défaut.
    public KeyCombo ToggleBoardKey { get; set; } = DefaultShortcut(ShortcutAction.ToggleBoard);
    public KeyCombo LockKey { get; set; } = DefaultShortcut(ShortcutAction.Lock);
    public KeyCombo PasteKey { get; set; } = DefaultShortcut(ShortcutAction.Paste);
    public KeyCombo UndoKey { get; set; } = DefaultShortcut(ShortcutAction.Undo);
    public KeyCombo DeleteKey { get; set; } = DefaultShortcut(ShortcutAction.Delete);
    public KeyCombo FitKey { get; set; } = DefaultShortcut(ShortcutAction.Fit);
    public KeyCombo CropKey { get; set; } = DefaultShortcut(ShortcutAction.Crop);

    /// <summary>Situations du jeu pendant lesquelles le tableau est masqué automatiquement.</summary>
    public HideSituation AutoHide { get; set; } = HideSituation.Cutscene | HideSituation.GameUiHidden;

    /// <summary>Raccourcis de départ : ceux de l'édition (coller, annuler, supprimer), aucun pour le reste.</summary>
    public static KeyCombo DefaultShortcut(ShortcutAction action) => action switch
    {
        ShortcutAction.Paste => new KeyCombo { Key = VirtualKey.V, Ctrl = true },
        ShortcutAction.Undo => new KeyCombo { Key = VirtualKey.Z, Ctrl = true },
        ShortcutAction.Delete => new KeyCombo { Key = VirtualKey.DELETE },
        _ => new KeyCombo(),
    };

    public KeyCombo Shortcut(ShortcutAction action) => action switch
    {
        ShortcutAction.ToggleBoard => ToggleBoardKey,
        ShortcutAction.Lock => LockKey,
        ShortcutAction.Paste => PasteKey,
        ShortcutAction.Undo => UndoKey,
        ShortcutAction.Delete => DeleteKey,
        ShortcutAction.Crop => CropKey,
        _ => FitKey,
    };

    public void SetShortcut(ShortcutAction action, KeyCombo combo)
    {
        switch (action)
        {
            case ShortcutAction.ToggleBoard:
                ToggleBoardKey = combo;
                break;
            case ShortcutAction.Lock:
                LockKey = combo;
                break;
            case ShortcutAction.Paste:
                PasteKey = combo;
                break;
            case ShortcutAction.Undo:
                UndoKey = combo;
                break;
            case ShortcutAction.Delete:
                DeleteKey = combo;
                break;
            case ShortcutAction.Crop:
                CropKey = combo;
                break;
            default:
                FitKey = combo;
                break;
        }
    }

    /// <summary>Autre action qui a la même combinaison (null s'il n'y en a pas).</summary>
    public ShortcutAction? SharedShortcut(ShortcutAction action) =>
        Enum.GetValues<ShortcutAction>().Where(other => other != action && Shortcut(other).SameAs(Shortcut(action)))
            .Select(other => (ShortcutAction?)other).FirstOrDefault();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
