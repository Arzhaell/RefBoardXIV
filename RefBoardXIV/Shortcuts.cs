using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace RefBoardXIV;

public enum ShortcutAction
{
    ToggleBoard,
    Lock,
    Paste,
    Undo,
    Delete,
    Fit,
    Crop,
}

/// <summary>
/// Les raccourcis clavier, lus une fois par image. Les touches sont lues directement sur le clavier (que ce soit le jeu
/// ou une fenêtre de Dalamud qui ait le clavier), seulement quand le jeu est la fenêtre active et que tu n'es pas en
/// train d'écrire (discussion du jeu, champ de texte).
/// </summary>
internal sealed class Shortcuts
{
    private readonly Configuration config;
    private readonly Dictionary<ShortcutAction, HotkeyWatcher> watchers =
        Enum.GetValues<ShortcutAction>().ToDictionary(action => action, _ => new HotkeyWatcher());
    private readonly HashSet<ShortcutAction> pressed = [];

    public Shortcuts(Configuration config) => this.config = config;

    /// <summary>Raccourcis utilisables partout dans le jeu ; les autres ne servent que sur le tableau.</summary>
    public static readonly ShortcutAction[] GameActions = [ShortcutAction.ToggleBoard, ShortcutAction.Lock];

    public static readonly ShortcutAction[] BoardActions =
        [ShortcutAction.Paste, ShortcutAction.Undo, ShortcutAction.Delete, ShortcutAction.Crop, ShortcutAction.Fit];

    /// <summary>Pendant qu'on choisit un raccourci dans les réglages, aucun ne se déclenche.</summary>
    public bool Suspended { get; set; }

    /// <summary>Le jeu est la fenêtre active et tu n'es pas en train d'écrire : les touches sont pour nous.</summary>
    public bool Listening { get; private set; }

    /// <summary>« Ctrl + Maj + L », ou « Aucun ».</summary>
    public static string Describe(KeyCombo combo)
    {
        if (!combo.IsSet)
            return Loc.T("None", "Aucun");
        var parts = new List<string>();
        if (combo.Ctrl)
            parts.Add("Ctrl");
        if (combo.Shift)
            parts.Add(Loc.T("Shift", "Maj"));
        if (combo.Alt)
            parts.Add("Alt");
        parts.Add(combo.Key == VirtualKey.DELETE ? Loc.T("Del", "Suppr") : combo.Key.GetFancyName());
        return string.Join(" + ", parts);
    }

    /// <summary>Raccourci d'une action pour un menu (vide s'il n'y en a pas).</summary>
    public string MenuLabel(ShortcutAction action) => config.Shortcut(action) is { IsSet: true } combo ? Describe(combo) : "";

    /// <summary>La combinaison de cette action vient d'être enfoncée (valable pendant toute l'image).</summary>
    public bool WasPressed(ShortcutAction action) => pressed.Contains(action);

    /// <summary>À appeler une fois par image, avant de dessiner les fenêtres.</summary>
    public void Update()
    {
        pressed.Clear();
        Listening = GameIsActive() && !IsTyping();
        foreach (var (action, watcher) in watchers)
        {
            // Les touches sont suivies même suspendues : une combinaison encore tenue après son choix ne se déclenche pas.
            if (watcher.Update(Listening && config.Shortcut(action).IsDown(IsKeyDown)) && !Suspended)
                pressed.Add(action);
        }
    }

    public static bool IsKeyDown(VirtualKey key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static bool GameIsActive()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static unsafe bool IsTyping()
    {
        if (ImGui.GetIO().WantTextInput)
            return true;
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
