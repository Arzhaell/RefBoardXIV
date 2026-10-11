namespace RefBoardXIV;

internal enum BoardCommand
{
    Toggle,
    Show,
    Hide,
    Lock,
    Settings,
    Unknown,
}

/// <summary>Ce qui suit /refboard : rien (afficher ou masquer le tableau), ou un mot en anglais ou en français.</summary>
internal static class Commands
{
    public static BoardCommand Parse(string arguments) => arguments.Trim().ToLowerInvariant() switch
    {
        "" or "toggle" => BoardCommand.Toggle,
        "show" or "afficher" => BoardCommand.Show,
        "hide" or "masquer" => BoardCommand.Hide,
        "lock" or "verrouiller" => BoardCommand.Lock,
        "config" or "settings" or "reglages" or "réglages" => BoardCommand.Settings,
        _ => BoardCommand.Unknown,
    };
}
