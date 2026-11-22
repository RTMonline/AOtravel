AO# plugin for automating travel options in Anarchy Online. Copy the contents to a new folder (for instance: travel) in your local AO# repo, then add the plugin like any other AO# plugin and inject 'Travel' on each character.

Inject like any other AO# plugin: \plugin\release\Travel\TravelPlugin.dll
Injecting the plugin will open and close a bag called 'Travel' in inventory. 
This is to index the contents. I haven't figured out how to verify contents without opening it. 

The plugin is active on inject and saves settings in: %USERNAME%\APPDATA\Local\AOSharp\Travel

Type /travel to open the menu ingame. 

There are 5 optional hotkeys, click the type to toggle between item, nano and camp. 
'Item' allows selection in a bag named 'Travel' in inventory. Like a grid can or beacon. 
'Nano' can select a yalm nano. (It can select any nano, but you get it). 
'Camp' will post /camp to all injected characters.

Ctrl+end is fixed to quit all characters. (same as alt+F4 alt+F4)

For some reason, the old-school yalm is not usable from a bag, so not an option atm.

I know the GUI sucks, not my specialty. Live with it.
