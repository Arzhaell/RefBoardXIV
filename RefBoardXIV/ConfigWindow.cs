using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace RefBoardXIV;

internal sealed class ConfigWindow : Window
{
    private static readonly Vector4 Accent = new(1f, 0.82f, 0.35f, 1f);

    // Largeur des textes d'aide.
    private const float HelpWidth = 480f;

    // Touches qui ne peuvent pas servir de raccourci : les modificateurs (pris à part), les boutons principaux de la
    // souris, les touches Windows, et Échap (qui annule le choix).
    private static readonly VirtualKey[] ExcludedKeys =
    [
        VirtualKey.NO_KEY, VirtualKey.LBUTTON, VirtualKey.RBUTTON, VirtualKey.MBUTTON, VirtualKey.ESCAPE,
        VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.MENU, VirtualKey.LCONTROL, VirtualKey.RCONTROL,
        VirtualKey.LSHIFT, VirtualKey.RSHIFT, VirtualKey.LMENU, VirtualKey.RMENU, VirtualKey.LWIN, VirtualKey.RWIN,
    ];

    private static readonly HideSituation[] Situations =
        [HideSituation.Cutscene, HideSituation.GPose, HideSituation.Combat, HideSituation.Loading, HideSituation.GameUiHidden];

    private readonly Plugin plugin;
    private VirtualKey[]? captureKeys;

    // Raccourci en cours de choix : on attend que toutes les touches soient relâchées, puis la combinaison suivante.
    private ShortcutAction? capturing;
    private bool captureArmed;
    private bool shortcutsTabDrawn;

    public ConfigWindow(Plugin plugin)
        : base("RefBoardXIV###RefBoardXIVConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
    }

    private Configuration Config => plugin.Configuration;

    public override void Draw()
    {
        shortcutsTabDrawn = false;
        if (ImGui.BeginTabBar("##Tabs"))
        {
            if (ImGui.BeginTabItem(Loc.T("Board", "Tableau")))
            {
                DrawBoardTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T("Shortcuts", "Raccourcis")))
            {
                DrawShortcutsTab();
                shortcutsTabDrawn = true;
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Onglet quitté pendant un choix de raccourci : le choix est abandonné.
        if (!shortcutsTabDrawn)
            StopCapture();

        // Échap annule un choix sans fermer la fenêtre : la fermeture par Échap revient une fois la touche relâchée.
        if (capturing == null && !RespectCloseHotkey && !Shortcuts.IsKeyDown(VirtualKey.ESCAPE))
            RespectCloseHotkey = true;

        ImGui.Spacing();
        ImGui.Separator();
        DrawLanguage();
    }

    public override void OnClose() => StopCapture();

    // Texte d'aide en gris, coupé à la largeur de la fenêtre.
    private static void Help(string text)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (HelpWidth * ImGuiHelpers.GlobalScale));
        ImGui.TextColored(ImGuiColors.DalamudGrey, text);
        ImGui.PopTextWrapPos();
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Accent, title);
        ImGui.Separator();
    }

    private void DrawBoardTab()
    {
        Help(Loc.T(
            "Your reference board, inside the game. /refboard shows or hides it (it also works in a macro).",
            "Ton tableau de références, dans le jeu. /refboard l'affiche ou le masque (ça marche aussi dans une macro)."));
        ImGui.Spacing();
        var open = plugin.Visibility.IsOpen;
        ImGui.BeginDisabled(!BoardVisibility.CanShow);
        if (ImGui.Button(open ? Loc.T("Hide the board", "Masquer le tableau") : Loc.T("Show the board", "Afficher le tableau")))
            plugin.Visibility.SetOpen(!open);
        ImGui.EndDisabled();
        if (!BoardVisibility.CanShow)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("(log in to a character first)", "(connecte-toi d'abord à un personnage)"));
        }
        else if (plugin.Visibility.IsAutoHidden)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.T("(hidden automatically)", "(masqué automatiquement)"));
        }

        Section(Loc.T("Board", "Tableau"));
        DrawBoardSettings();

        Section(Loc.T("Hide automatically", "Masquer automatiquement"));
        DrawAutoHide();
    }

    private void DrawBoardSettings()
    {
        Help(Loc.T(
            "Drag images from the Windows explorer, or paste them. Mouse wheel: zoom. Drag the background: move around. "
            + "Drag an image or its corners: move or resize it. Right click: menu. The keyboard shortcuts are in their own tab.",
            "Glisse des images depuis l'explorateur de Windows, ou colle-les. Molette : zoom. Faire glisser le fond : se déplacer. "
            + "Faire glisser une image ou ses coins : la déplacer ou la redimensionner. Clic droit : menu. Les raccourcis clavier ont leur onglet."));
        ImGui.Spacing();

        var locked = Config.Locked;
        if (ImGui.Checkbox(Loc.T("Lock the board", "Verrouiller le tableau"), ref locked))
            plugin.SetLocked(locked);
        Help(Loc.T(
            "Only what is inside the blue frame stays, in place (no title bar, buttons or background), and your clicks go to the game. "
            + "To unlock: this box, its shortcut or /refboard lock.",
            "Il ne reste que ce qui est dans le cadre bleu, à sa place (ni barre de titre, ni boutons, ni fond), et tes clics vont au jeu. "
            + "Pour déverrouiller : cette case, son raccourci ou /refboard verrouiller."));

        var frameWhenLocked = Config.FrameWhenLocked;
        if (ImGui.Checkbox(Loc.T("Keep the frame visible when locked", "Garder le cadre visible une fois verrouillé"), ref frameWhenLocked))
        {
            Config.FrameWhenLocked = frameWhenLocked;
            Config.Save();
        }

        var opacity = Config.ImageOpacity * 100f;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat(Loc.T("Image opacity", "Opacité des images"), ref opacity, 10f, 100f, "%.0f %%"))
            Config.ImageOpacity = Math.Clamp(opacity / 100f, 0.1f, 1f);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Config.Save();
    }

    private void DrawAutoHide()
    {
        Help(Loc.T(
            "The board comes back when the situation ends, unless you showed or hid it yourself in the meantime.",
            "Le tableau revient quand la situation se termine, sauf si tu l'as affiché ou masqué toi-même entre-temps."));
        foreach (var situation in Situations)
        {
            var hidden = Config.AutoHide.HasFlag(situation);
            if (ImGui.Checkbox(SituationName(situation), ref hidden))
            {
                Config.AutoHide = hidden ? Config.AutoHide | situation : Config.AutoHide & ~situation;
                Config.Save();
            }
        }
    }

    private void DrawShortcutsTab()
    {
        UpdateCapture();
        Help(Loc.T(
            "Click a shortcut, then press the combination you want (Esc: cancel). The game receives these keys too: "
            + "choose combinations it doesn't use. Shortcuts don't trigger while you're typing (chat, text field).",
            "Clique sur un raccourci, puis appuie sur la combinaison voulue (Échap : annuler). Le jeu reçoit aussi ces touches : "
            + "choisis des combinaisons qui n'y servent pas. Les raccourcis ne se déclenchent pas pendant que tu écris (discussion, champ de texte)."));

        Section(Loc.T("Anywhere in the game", "Partout dans le jeu"));
        foreach (var action in Shortcuts.GameActions)
            DrawShortcut(action);

        Section(Loc.T("On the board", "Sur le tableau"));
        Help(Loc.T(
            "When the mouse is over the board (or you clicked it last), board unlocked.",
            "Quand la souris est sur le tableau (ou que tu l'as cliqué en dernier), tableau déverrouillé."));
        foreach (var action in Shortcuts.BoardActions)
            DrawShortcut(action);
    }

    private void DrawShortcut(ShortcutAction action)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushID((int)action);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(ActionName(action));

        ImGui.SameLine(200 * scale);
        // Le clic change « capturing » : la couleur est retirée selon l'état d'avant le clic, sinon on retirerait une
        // couleur jamais mise (celle de Dalamud), ce qui efface le texte de toutes les fenêtres.
        var isCapturing = capturing == action;
        var label = isCapturing ? Loc.T("Press a key… (Esc: cancel)", "Appuie sur une touche… (Échap : annuler)") : ComboName(Config.Shortcut(action));
        if (isCapturing)
            ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        var clicked = ImGui.Button(label + "##Set", new Vector2(230 * scale, 0));
        if (isCapturing)
            ImGui.PopStyleColor();
        if (clicked)
            StartCapture(action);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            SetShortcut(action, new KeyCombo());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("No shortcut", "Aucun raccourci"));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
            SetShortcut(action, Configuration.DefaultShortcut(action));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T($"Default: {ComboName(Configuration.DefaultShortcut(action))}", $"Par défaut : {ComboName(Configuration.DefaultShortcut(action))}"));

        if (Config.SharedShortcut(action) is { } other)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.T($"also used by: {ActionName(other)}", $"sert aussi à : {ActionName(other)}"));
        }

        ImGui.PopID();
    }

    private void StartCapture(ShortcutAction action)
    {
        capturing = action;
        captureArmed = false;
        plugin.Shortcuts.Suspended = true;
        RespectCloseHotkey = false;
    }

    private void StopCapture()
    {
        if (capturing == null)
            return;
        capturing = null;
        plugin.Shortcuts.Suspended = false;
    }

    // Lit le clavier pendant un choix de raccourci : la première touche enfoncée (hors modificateurs), avec les
    // modificateurs tenus à ce moment-là.
    private void UpdateCapture()
    {
        if (capturing is not { } action)
            return;
        if (Shortcuts.IsKeyDown(VirtualKey.ESCAPE))
        {
            StopCapture();
            return;
        }

        captureKeys ??= Plugin.KeyState.GetValidVirtualKeys().Where(key => !ExcludedKeys.Contains(key)).ToArray();
        var key = captureKeys.FirstOrDefault(Shortcuts.IsKeyDown);
        if (key == VirtualKey.NO_KEY)
        {
            captureArmed = true;
            return;
        }

        if (!captureArmed)
            return;
        SetShortcut(action, new KeyCombo
        {
            Key = key,
            Ctrl = Shortcuts.IsKeyDown(VirtualKey.CONTROL),
            Shift = Shortcuts.IsKeyDown(VirtualKey.SHIFT),
            Alt = Shortcuts.IsKeyDown(VirtualKey.MENU),
        });
        StopCapture();
    }

    private void SetShortcut(ShortcutAction action, KeyCombo combo)
    {
        Config.SetShortcut(action, combo);
        Config.Save();
    }

    private static string ComboName(KeyCombo combo) => Shortcuts.Describe(combo);

    private static string ActionName(ShortcutAction action) => action switch
    {
        ShortcutAction.ToggleBoard => Loc.T("Show / hide the board", "Afficher / masquer le tableau"),
        ShortcutAction.Lock => Loc.T("Lock / unlock the board", "Verrouiller / déverrouiller"),
        ShortcutAction.Paste => Loc.T("Paste", "Coller"),
        ShortcutAction.Undo => Loc.T("Undo delete", "Annuler la suppression"),
        ShortcutAction.Delete => Loc.T("Remove the selected image", "Retirer l'image choisie"),
        ShortcutAction.Crop => Loc.T("Crop the selected image", "Rogner l'image choisie"),
        _ => Loc.T("Show everything", "Tout voir"),
    };

    private static string SituationName(HideSituation situation) => situation switch
    {
        HideSituation.Cutscene => Loc.T("During cutscenes", "Pendant les cinématiques"),
        HideSituation.GPose => Loc.T("In gpose", "En gpose"),
        HideSituation.Combat => Loc.T("In combat", "En combat"),
        HideSituation.Loading => Loc.T("During loading screens", "Pendant les chargements"),
        _ => Loc.T("When the game UI is hidden", "Quand l'interface du jeu est masquée"),
    };

    private void DrawLanguage()
    {
        var current = Config.Language;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo(Loc.T("Language", "Langue"), LanguageName(current)))
            return;

        foreach (var language in Enum.GetValues<PluginLanguage>())
        {
            if (ImGui.Selectable(LanguageName(language), language == current))
                plugin.SetLanguage(language);
        }

        ImGui.EndCombo();
    }

    private static string LanguageName(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "English",
        PluginLanguage.French => "Français",
        _ => Loc.T("Automatic (Dalamud)", "Automatique (Dalamud)"),
    };
}
