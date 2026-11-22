# Travel 2.4 - AO# plugin

(Earlier builds are kept in `release/Travel-1.0`, `-2.0`, `-2.1`, `-2.2` and `-2.3`.)

## New in 2.4
- Each of the five keys can now be set to **Camp**: the Nano/Item button cycles Nano -> Item -> Camp. Pressing that key makes every injected character (with "Join in" ticked) /camp, the game's normal camp-and-log-out to character select. "(clear this key)" in a Camp row's list turns it back into an empty nano key.
- New sixth key **Ctrl+End** = /quit on every injected character (fixed, not configurable; the "Join in" box doesn't apply to it). It uses AO#'s own quit (Game.Quit), the same call the AO# multibox sync plugin uses for "log all out".

## New in 2.3
- The window's title bar shows the version: "Travel 2.3".

## New in 2.2
- The pick lists no longer have a fixed size (2.1 forced 330 x 24, which left an empty gap). Like other AO# plugin windows, each list now sits in a column that stretches with the window, and keeps its natural height.
- Opens at about 600 x 700; can be dragged down to 460 x 560. If it still looks wrong, 2.0 (small window) is kept in `release/Travel-2.0`.

## New in 2.1
- Drag its edge to the size you like: each character remembers its own window size.

Multibox travel hotkeys for Anarchy Online.

## Install
1. Copy the folder `release/Travel` somewhere permanent (keep `TravelPlugin.dll` and the `UI` folder together).
2. In the AO# launcher, add `TravelPlugin.dll` to the profile you inject on your characters.
3. Inject. Each character prints "[Travel] 1.0 active" in chat. The window stays closed.

## Use
- Type `/travel` on a character to open (or close) its Travel window. Only that character's window opens.
- Each row is one hotkey: Ctrl+I, Ctrl+O, Ctrl+P, Ctrl+L, Ctrl+G.
  - The Nano/Item button switches what the row uses.
  - The list shows that character's nanos (travel nanos first; type in "Nano filter" and press "Refresh lists" to narrow it)
    or the items in its inventory bag named "Travel".
  - "Test" fires the row on this character only.
- Press a hotkey in any character's game window: every injected character with "Join in" ticked fires its own pick for that row,
  in a team or not. Each one writes what it did in its chat; the open Travel window lists all results.
- Picks are saved per character the same way other AO# plugins do (e.g. ManagerGMI):
  `%LOCALAPPDATA%\AOSharp\Travel\<character id>\Config.json` - outside the plugin and repository folders.
  They save on every change and load automatically on inject; chat says "[Travel] Settings loaded ...".
  Moving or updating the plugin keeps them.

## Cancelling a travel nano (new in 2.0)
- Press the same hotkey again while its travel nano is running: every character stops its travel nano for that key.
  The character you press on decides: if its nano for that key is running, all characters cancel; otherwise all travel.
- `/travel off` (or the window's "Cancel travel nanos (all characters)" button) stops every travel nano set on any key, on all characters.
- The Test button works the same way on one character: it casts, or cancels if the nano is running.
- Only nanos are cancelled; items from the Travel bag are never touched.
- Cancelling removes the running nano from the character the way other AO# plugins cancel buffs (Buff.Remove).
  The window shows "RUNNING, press the key to cancel" next to a key whose nano is on.

## Notes
- The bag must be named exactly "Travel" (any capitals). The game only knows a bag's contents after it has been opened, so about 2 seconds after inject Travel opens the bag (by using it, like other AO# plugins do), waits for the contents, then closes it again. If a key press still finds the bag unloaded, it opens it, waits up to 8 seconds and then uses the item.
- Nanos are cast on yourself through the game's own cast function (the same as clicking the nano). The plugin never blocks a cast on its own checks (AO#'s requirement check wrongly rejected some nanos): the game decides, and any game message right after the cast (for example why it failed) is shown in the Travel window too. While moving, sitting or already casting, Travel waits up to 3 seconds, then tries anyway.
- Characters talk to each other the AO# way (local network channel 117), so all clients must run on the same PC or LAN.

## Test settings storage
`dotnet build -c Release` then run `ConfigTestin\Release
et48\ConfigTest.exe` (save + reload check, prints PASS).

## Rebuild
`dotnet build -c Release` in `TravelPlugin` (targets .NET Framework 4.8; references the AO# DLLs copied in `lib`).
