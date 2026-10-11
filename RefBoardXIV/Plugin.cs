using System.IO;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace RefBoardXIV;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/refboard";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IDragDropManager DragDrop { get; private set; } = null!;
    [PluginService] internal static INotificationManager Notifications { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("RefBoardXIV");
    private readonly ConfigWindow configWindow;
    private readonly BoardWindow boardWindow;

    public Plugin()
    {
        Configuration = LoadConfiguration();
        Loc.Update(Configuration.Language);
        Store = new BoardStore(Path.Combine(PluginInterface.ConfigDirectory.FullName, "boards"), Warn);
        if (Store.Boards.Count == 0)
            Store.Create(NewBoardName());

        Shortcuts = new Shortcuts(Configuration);
        boardWindow = new BoardWindow(this);
        Visibility = new BoardVisibility(Configuration, boardWindow);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(boardWindow);
        windowSystem.AddWindow(configWindow);

        // Le tableau suit ses propres cases « Masquer automatiquement », pas le réglage général de Dalamud.
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
        PluginInterface.UiBuilder.DisableUserUiHide = true;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += Visibility.Toggle;
        PluginInterface.LanguageChanged += OnDalamudLanguageChanged;

        RegisterCommand();
    }

    public Configuration Configuration { get; }

    internal BoardStore Store { get; }

    internal BoardVisibility Visibility { get; }

    internal Shortcuts Shortcuts { get; }

    /// <summary>Fenêtre « Parcourir… » pour choisir des images.</summary>
    internal FileDialogManager FileDialog { get; } = new();

    /// <summary>Tableau affiché : celui choisi la dernière fois, sinon le premier.</summary>
    internal Board CurrentBoard => Store.Boards.FirstOrDefault(b => b.Id == Configuration.CurrentBoard) ?? Store.Boards[0];

    internal void SelectBoard(Board board)
    {
        if (board == CurrentBoard && Configuration.CurrentBoard == board.Id)
            return;
        boardWindow.OnBoardChanged();
        Configuration.CurrentBoard = board.Id;
        Configuration.Save();
    }

    /// <summary>Supprime un tableau ; s'il n'en reste aucun, un tableau vide le remplace.</summary>
    internal void DeleteBoard(Board board)
    {
        Store.Delete(board);
        if (Store.Boards.Count == 0)
            Store.Create(NewBoardName());
        SelectBoard(Store.Boards[0]);
    }

    /// <summary>« Tableau 1 », « Tableau 2 »… : le premier nom pas encore pris.</summary>
    internal string NewBoardName()
    {
        var prefix = Loc.T("Board", "Tableau");
        var number = 1;
        while (Store.Boards.Any(b => b.Name == $"{prefix} {number}"))
            number++;
        return $"{prefix} {number}";
    }

    internal void SetLocked(bool locked)
    {
        if (Configuration.Locked == locked)
            return;
        Configuration.Locked = locked;
        Configuration.Save();
    }

    public void SetLanguage(PluginLanguage language)
    {
        Configuration.Language = language;
        Configuration.Save();
        RefreshLanguage();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.LanguageChanged -= OnDalamudLanguageChanged;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= Visibility.Toggle;
        windowSystem.RemoveAllWindows();
        Visibility.Dispose();
        boardWindow.Flush();
    }

    // Un fichier de configuration illisible ne doit pas empêcher le plugin de se charger : on repart des réglages par défaut.
    private static Configuration LoadConfiguration()
    {
        try
        {
            return PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        catch (System.Exception e)
        {
            Log.Error(e, "Could not read the configuration, falling back to default settings.");
            return new Configuration();
        }
    }

    private static void Warn(string message)
    {
        Log.Warning(message);
        ChatGui.PrintError($"RefBoardXIV : {message}");
    }

    private void OnDraw()
    {
        Shortcuts.Update();
        if (Shortcuts.WasPressed(ShortcutAction.ToggleBoard))
            Visibility.Toggle();
        if (Shortcuts.WasPressed(ShortcutAction.Lock))
            SetLocked(!Configuration.Locked);

        windowSystem.Draw();
        FileDialog.Draw();
    }

    private void OnCommand(string command, string arguments)
    {
        switch (Commands.Parse(arguments))
        {
            case BoardCommand.Toggle:
                Visibility.Toggle();
                break;
            case BoardCommand.Show:
                Visibility.SetOpen(true);
                break;
            case BoardCommand.Hide:
                Visibility.SetOpen(false);
                break;
            case BoardCommand.Lock:
                SetLocked(!Configuration.Locked);
                break;
            case BoardCommand.Settings:
                configWindow.Toggle();
                break;
            default:
                ChatGui.Print(CommandHelp);
                break;
        }
    }

    private static string CommandHelp => Loc.T(
        "/refboard: shows or hides your board. /refboard lock (clicks go to the game), /refboard config (settings).",
        "/refboard : affiche ou masque ton tableau. /refboard verrouiller (les clics vont au jeu), /refboard réglages.");

    private void OnDalamudLanguageChanged(string languageCode)
    {
        if (Configuration.Language == PluginLanguage.Auto)
            RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        Loc.Update(Configuration.Language);
        CommandManager.RemoveHandler(CommandName);
        RegisterCommand();
    }

    private void RegisterCommand()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = CommandHelp });
    }
}
