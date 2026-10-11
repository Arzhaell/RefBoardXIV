# RefBoardXIV

🇫🇷 [Version française](README.fr.md)

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV: a **reference board** in a game
window. Drop your images on it, zoom, move them around, then lock it: only the images stay on screen and your clicks
go to the game.

## Installation
1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL, click **+**, tick the checkbox and save:
   ```
   https://raw.githubusercontent.com/Arzhaell/RefBoardXIV/main/repo.json
   ```
3. Type `/xlplugins`, search for **RefBoardXIV** and install it.

## Usage
- **Add images**: drag them from the Windows explorer, paste them (Ctrl+V, from a web browser or copied files), or
  use the "Add images…" button. Each image is copied into the board: it does not break if you move the original
  files.
- **Get around**: mouse wheel to zoom, drag the background (or middle / right click) to pan, double-click the
  background to see everything.
- **Arrange**: drag an image to move it, its corners to resize it; right-click it for its original size, bring to
  front / send to back, delete. Del removes the selected image, Ctrl+Z brings it back.
- **Crop**: right-click an image → "Crop" (or the toolbar button, or its shortcut), then drag its edges; Enter or a
  click outside to finish, Esc to cancel, "Whole image" to undo the crop.
- **Several boards**, each with its own name; everything is saved automatically. Pasted images are saved as PNG.
- **Share a board**: the share button saves it in a single `.refboard` file (layout, crops and images) and shows it
  in the explorer, ready to send (Discord…). To open a board you received: "Import a board…" at the bottom of the
  board list, or drag the file onto your board. It arrives as a new board.
- **Locked mode**: the window is the frame. A fixed blue frame (with its size in pixels) shows the image area: once
  locked, exactly what is inside stays, at the same place (no title bar, buttons or background), and clicks go to the
  game. The frame can stay visible when locked (setting).
- `/refboard` shows or hides the board (works in a macro too); `/refboard lock`, `/refboard config`. The board never
  opens by itself: it shows when you ask for it, and only when you are logged in to a character.
- **Keyboard shortcuts**, chosen in the "Shortcuts" tab of the settings: show / hide and lock (anywhere in the game),
  paste, undo, remove the image, crop, show everything (on the board). They do not fire while you type in the chat.
- **Hide automatically**, as you choose: cutscenes, gpose, combat, loading screens, game UI hidden.
- Image opacity can be lowered in the settings.

Boards are saved in `%APPDATA%\XIVLauncher\pluginConfigs\RefBoardXIV\boards\` (one folder per board).

The plugin follows the Dalamud language (English or French); the settings let you force it.

## Disclaimer
Like any third-party tool, using Dalamud and its plugins is against the FFXIV Terms of Service. Use it at your own
risk.

## Building and testing
- `dotnet test RefBoardXIV.Tests -c Release`: tests of the logic (board saving, view, cropping, image sizes,
  clipboard, PNG, sharing, shortcuts, commands, automatic hiding).
- `dotnet build RefBoardXIV -c Release`: to try it, add `RefBoardXIV\bin\Release\RefBoardXIV.dll` to the dev plugin
  locations (`/xlsettings`, **Experimental** tab); Dalamud reloads it by itself, even with the game running. It does
  not load the version installed from the repository at the same time: disable that one.

## Releasing a new version
Pushing an annotated `vX.Y.Z` tag is all it takes: the GitHub workflow runs the tests, builds the plugin, creates
the release (its notes are the tag message) and updates `repo.json`.

```bash
git tag -a v1.2.0 --cleanup=verbatim -F notes.txt
git push origin v1.2.0
```

## Credits
- [Dalamud](https://github.com/goatcorp/Dalamud) and [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs):
  plugin framework and game structures.
- [xUnit](https://xunit.net/) for the tests.
- Idea inspired by [PureRef](https://www.pureref.com/), software by Idyllic Pixel (PureRef is their trademark). Not
  affiliated with PureRef: this plugin contains nothing from PureRef and does not read its `.pur` files.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. This plugin is neither affiliated with nor endorsed by Square Enix.
