using Dalamud.Game.ClientState.Keys;
using Newtonsoft.Json;

namespace RefBoardXIV.Tests;

public class HotkeyTests
{
    private static Func<VirtualKey, bool> Pressed(params VirtualKey[] keys) => keys.Contains;

    private static KeyCombo CtrlP() => new() { Key = VirtualKey.P, Ctrl = true };

    [Fact]
    public void RecognizesExactCombination()
    {
        Assert.True(CtrlP().IsDown(Pressed(VirtualKey.CONTROL, VirtualKey.P)));
    }

    [Fact]
    public void RequiresChosenModifiers()
    {
        Assert.False(CtrlP().IsDown(Pressed(VirtualKey.P)));
    }

    [Fact]
    public void RejectsExtraModifiers()
    {
        Assert.False(CtrlP().IsDown(Pressed(VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.P)));
        Assert.False(CtrlP().IsDown(Pressed(VirtualKey.CONTROL, VirtualKey.MENU, VirtualKey.P)));
    }

    [Fact]
    public void NoKeyMeansNoShortcut()
    {
        var combo = new KeyCombo();
        Assert.False(combo.IsDown(Pressed(VirtualKey.NO_KEY)));
        Assert.False(combo.IsDown(_ => true));
        Assert.False(combo.SameAs(new KeyCombo()));
    }

    [Fact]
    public void FiresOncePerPress()
    {
        var watcher = new HotkeyWatcher();
        Assert.True(watcher.Update(true));
        Assert.False(watcher.Update(true));
        Assert.False(watcher.Update(true));
        Assert.False(watcher.Update(false));
        Assert.True(watcher.Update(true));
    }

    [Fact]
    public void DefaultsAreEditingShortcuts()
    {
        var config = new Configuration();
        Assert.True(config.Shortcut(ShortcutAction.Paste).SameAs(new KeyCombo { Key = VirtualKey.V, Ctrl = true }));
        Assert.True(config.Shortcut(ShortcutAction.Undo).SameAs(new KeyCombo { Key = VirtualKey.Z, Ctrl = true }));
        Assert.True(config.Shortcut(ShortcutAction.Delete).SameAs(new KeyCombo { Key = VirtualKey.DELETE }));
        Assert.False(config.Shortcut(ShortcutAction.Lock).IsSet);
        Assert.False(config.Shortcut(ShortcutAction.ToggleBoard).IsSet);
        Assert.False(config.Shortcut(ShortcutAction.Fit).IsSet);
    }

    [Fact]
    public void EachActionKeepsItsOwnShortcut()
    {
        var config = new Configuration();
        foreach (var action in Enum.GetValues<ShortcutAction>())
            config.SetShortcut(action, new KeyCombo { Key = (VirtualKey)((int)VirtualKey.F1 + (int)action) });
        foreach (var action in Enum.GetValues<ShortcutAction>())
            Assert.Equal((VirtualKey)((int)VirtualKey.F1 + (int)action), config.Shortcut(action).Key);
    }

    [Fact]
    public void SharedShortcutIsReported()
    {
        var config = new Configuration();
        config.SetShortcut(ShortcutAction.Lock, new KeyCombo { Key = VirtualKey.V, Ctrl = true });
        Assert.Equal(ShortcutAction.Paste, config.SharedShortcut(ShortcutAction.Lock));
        Assert.Equal(ShortcutAction.Lock, config.SharedShortcut(ShortcutAction.Paste));
        Assert.Null(config.SharedShortcut(ShortcutAction.Undo));
        // Deux actions sans raccourci ne se gênent pas.
        Assert.Null(config.SharedShortcut(ShortcutAction.Fit));
    }

    [Fact]
    public void ClearedShortcutStaysClearedAfterReload()
    {
        // Mêmes réglages de lecture que Dalamud pour les fichiers de configuration.
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };
        var config = new Configuration();
        config.SetShortcut(ShortcutAction.Paste, new KeyCombo());
        config.SetShortcut(ShortcutAction.Lock, new KeyCombo { Key = VirtualKey.L, Alt = true });

        var reloaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config, settings), settings)!;

        Assert.False(reloaded.Shortcut(ShortcutAction.Paste).IsSet);
        Assert.True(reloaded.Shortcut(ShortcutAction.Lock).SameAs(new KeyCombo { Key = VirtualKey.L, Alt = true }));
        Assert.True(reloaded.Shortcut(ShortcutAction.Undo).SameAs(Configuration.DefaultShortcut(ShortcutAction.Undo)));
    }
}
