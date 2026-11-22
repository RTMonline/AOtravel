# Travel plugin 2.4 - mechanics sheet

The real AO# plugin is in plugin/ (built file: plugin/release/Travel). The Rust program in this folder is the
look-alike mockup of its window, kept for previews and Craft Anything's checks.

## Plugin behaviour (plugin/TravelPlugin/Travel.cs)
| Value | Setting | Source |
|---|---|---|
| Hotkey rows | 5 | request; `Rows` |
| Hotkeys | Ctrl+I, Ctrl+O, Ctrl+P, Ctrl+L, Ctrl+G (static) | person's follow-up; `KeyNames`/`VirtualKeys` |
| Hotkey read | only while that game window is in front (GetForegroundWindow) | ManagerSync pattern in aosp-bots; builder decision |
| Repeat guard | 0.35 s between presses | *estimated*; `PressCooldown` |
| What a hotkey does | broadcasts the row to every injected character; each fires its own pick | request; `PressRow` / `OnPressReceived` |
| Team required | no (AO# IPC multicast, channel 117) | request; AO# IPCChannel |
| Nano source | `Spell.List` (the nano list) | request |
| Nano cast | game client cast function N3Msg_CastNanoSpell(nano, self); fallback Spell.Cast(self, set target) | AO# N3EngineClientAnarchy_t / CombatHandler; v1.0 raw cast was ignored by the game (person's report) |
| Nano cast checks | none block the cast: requirements only pick between same-named versions; ready and nano cost are shown as hints; waits up to 3 s to stand still, then casts anyway; game feedback relayed for 2 s | person's report: AO# MeetsSelfUseReqs rejected a castable nano; times *estimated* |
| Nano not ready | skipped and reported | builder decision; `Spell.IsReady` |
| Item source | inventory bag named "Travel" (any capitals), `Item.Use()` | request; AO# Backpack.Name |
| Bag contents loading | opens the Travel bag by using the bag item 2 s after inject, closes it 0.5 s after the contents arrive; a press on an unloaded bag waits up to 8 s (3 tries, 3 s apart) | aosp-bots "Crystal Filled by the Source" HandleBags pattern; delays *estimated* |
| Nano list order | travel nano lines first, then A-Z, max 150, optional filter | *estimated* |
| Cancel travel nano (2.0) | same hotkey again while that row's nano runs on the pressing character: cancel on all characters; nanos only, never items | person's v2 request; Travel.cs PressRow / CancelRow |
| Cancel all (2.0) | /travel off (also cancel, stop) or window button: stop every row's travel nano on all characters | builder decision |
| Cancel method | Buff.Remove() on matching own buffs: same nano id or name, or a new same-nano-line buff seen within 6 s of Travel's cast | aosp-bots CancelBuffs pattern; 6 s *estimated* |
| Already running on Use | skipped with a note (no recast) | builder decision |
| Window size (2.2) | opens 600 x 700, minimum 460 x 560, AutoScale + NoFade; last size saved per character; lists have no fixed size and stretch in a column (min 220 wide) | person's 2.1/2.2 feedback; layout pattern from aosp-bots DualEntryDropDown.xml; sizes *estimated* |
| Version numbering | +0.1 for each adjustment | person's 2.2 request |
| Window title (2.3) | "Travel <version>", e.g. Travel 2.3 | person's 2.3 request; Travel.cs WindowTitle |
| Camp option (2.4) | any of the 5 keys can be Nano, Item or Camp; Camp = AO# Game.Camp() (normal /camp) on every joining character | person's 2.4 request; AO# Game.cs |
| Quit key (2.4) | Ctrl+End, fixed: AO# Game.Quit() (/quit) on every injected character, ignores Join in | person's 2.4 request; same call as aosp-bots ManagerSync log-all-out |
| Open command | /travel toggles the window on that character only | request; `Chat.RegisterCommand("travel")` |
| Window on inject | closed; chat line "[Travel] 1.0 active" | request |
| Join in checkbox | default on, per character | *estimated* |
| Test button | fires the row on this character only | *estimated* |
| Results | each character writes to its own chat and reports to the open window (max 6 lines) | *estimated* |
| Settings saved | per character, %LOCALAPPDATA%\AOSharp\Travel\<character id>\Config.json (CharSettings keyed by character id), saved on every change, loaded on inject | same pattern as aosp-bots ManagerGMI Config.cs; plugin/TravelPlugin/Config.cs; ConfigTest PASS |

## Mockup-only values (Rust preview)
| Value | Setting | Source |
|---|---|---|
| Sample characters | 4 invented names | *estimated* |
| Character picker | dropdown marked "mockup only" | person's follow-up |
| Window size | 640 x 486 px | *estimated* |
