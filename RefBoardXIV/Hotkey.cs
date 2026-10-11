using System;
using Dalamud.Game.ClientState.Keys;

namespace RefBoardXIV;

/// <summary>Une combinaison de touches : une touche, avec ou sans Ctrl, Maj et Alt.</summary>
[Serializable]
public sealed class KeyCombo
{
    public VirtualKey Key { get; set; } = VirtualKey.NO_KEY;
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    public bool IsSet => Key != VirtualKey.NO_KEY;

    /// <summary>La touche est enfoncée, avec exactement les modificateurs choisis (ni plus, ni moins).</summary>
    public bool IsDown(Func<VirtualKey, bool> isKeyDown) =>
        IsSet
        && isKeyDown(Key)
        && isKeyDown(VirtualKey.CONTROL) == Ctrl
        && isKeyDown(VirtualKey.SHIFT) == Shift
        && isKeyDown(VirtualKey.MENU) == Alt;

    public bool SameAs(KeyCombo other) => IsSet && Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;

    public KeyCombo Copy() => new() { Key = Key, Ctrl = Ctrl, Shift = Shift, Alt = Alt };
}

/// <summary>Se déclenche une fois quand on enfonce la combinaison, pas en continu tant qu'on la tient.</summary>
internal sealed class HotkeyWatcher
{
    private bool wasDown;

    /// <summary>Vrai à l'image où la combinaison vient d'être enfoncée.</summary>
    public bool Update(bool down)
    {
        var pressed = down && !wasDown;
        wasDown = down;
        return pressed;
    }
}
