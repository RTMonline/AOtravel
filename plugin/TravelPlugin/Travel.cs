using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using TravelPlugin.IPCMessages;

namespace TravelPlugin
{
    /// <summary>
    /// Travel 1.0 - multibox travel hotkeys.
    /// Ctrl+I, Ctrl+O, Ctrl+P, Ctrl+L and Ctrl+G each fire one row on every injected character
    /// (team or not); every character uses its own pick for that row: a nano from its nano list
    /// or an item from its inventory bag named "Travel". /travel opens the settings window on the
    /// character where it is typed; the plugin is active from the moment it is injected.
    /// </summary>
    public class Travel : AOPluginEntry
    {
        public const string Version = "2.4";
        public const int Rows = 5;
        public static string WindowTitle => $"Travel {Version}";
        public static readonly string[] KeyNames = { "Ctrl+I", "Ctrl+O", "Ctrl+P", "Ctrl+L", "Ctrl+G" };
        private static readonly int[] VirtualKeys = { 0x49, 0x4F, 0x50, 0x4C, 0x47 }; // I O P L G

        // 2.4: a sixth, fixed hotkey that quits the game on every injected character (like typing /quit).
        public const string QuitKeyName = "Ctrl+End";
        private const int VK_END = 0x23;
        private const int QuitRow = -2;
        private bool _quitKeyWasDown;
        private const int VK_CONTROL = 0x11;
        private const byte IpcChannelId = 117;
        private const string TravelBagName = "Travel";
        private const int MaxListEntries = 150;
        private const double PressCooldown = 0.35;
        private const int ResultLines = 6;

        private static readonly NanoLine[] TravelNanoLines =
        {
            NanoLine.BeaconWarp, NanoLine.ShadowlandBindandRecall, NanoLine.SelfGrid, NanoLine.TeamGrid,
            NanoLine.EmergencyGrid, NanoLine.FixerGrid, NanoLine.GeneralRunspeedBuffs, NanoLine.RunspeedBuffs,
            NanoLine.TeamRunSpeedBuffs, NanoLine.FixerRunspeedBase
        };

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private Config _config;
        private TravelSettings _settings => _config.CharSettings[Game.ClientInst];
        private IPCChannel _ipc;
        private Window _window;
        private readonly Random _random = new Random();
        private readonly bool[] _keyWasDown = new bool[Rows];
        private double _lastPressTime = -10;
        private IntPtr _gameWindow = IntPtr.Zero;

        // window state
        private readonly DropdownMenu[] _dropdowns = new DropdownMenu[Rows];
        private readonly List<string>[] _entries = new List<string>[Rows];
        private readonly uint[] _lastSelection = new uint[Rows];
        private string _filter = "";

        // last key press shown in the window
        private int _lastPressId;
        private string _lastPressTitle = "Last key press: none yet";
        private readonly List<string> _lastResults = new List<string>();

        public override void Run()
        {
            try
            {
                LoadSettings();

                _ipc = new IPCChannel(IpcChannelId);
                _ipc.RegisterCallback((int)TravelOpcode.Press, OnPressReceived);
                _ipc.RegisterCallback((int)TravelOpcode.Result, OnResultReceived);

                Chat.RegisterCommand("travel", (command, args, chatWindow) =>
                {
                    string arg = args != null && args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
                    if (arg == "off" || arg == "cancel" || arg == "stop")
                        CancelAllEverywhere();
                    else
                        ToggleWindow();
                });

                Game.OnUpdate += OnUpdate;
                Chat.FeedbackReceived += OnFeedback;

                Chat.WriteLine($"[Travel] {Version} active. {string.Join(", ", KeyNames)} travel on all injected characters. Type /travel to set up this character.", ChatColor.LightBlue);
            }
            catch (Exception e)
            {
                Chat.WriteLine($"[Travel] Could not start: {e.Message}", ChatColor.Red);
            }
        }

        public override void Teardown()
        {
            Game.OnUpdate -= OnUpdate;
            Chat.FeedbackReceived -= OnFeedback;
            SaveSettings();
            if (_window != null && _window.IsValid)
                _window.Close();
        }

        // ------------------------------------------------------------------ hotkeys

        private bool IsGameWindowActive()
        {
            if (_gameWindow == IntPtr.Zero)
                _gameWindow = Process.GetCurrentProcess().MainWindowHandle;
            return _gameWindow != IntPtr.Zero && GetForegroundWindow() == _gameWindow;
        }

        private static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

        private void OnUpdate(object sender, float deltaTime)
        {
            try
            {
                PollHotkeys();
                PollWindow();
                PollTravelBag();
                PollPendingNano();
                PollBuffWatch();
            }
            catch (Exception e)
            {
                Logger?.Error(e.ToString());
            }
        }

        private void PollHotkeys()
        {
            bool active = IsGameWindowActive();
            bool ctrl = active && IsDown(VK_CONTROL);

            for (int row = 0; row < Rows; row++)
            {
                bool down = active && IsDown(VirtualKeys[row]);
                bool pressed = down && !_keyWasDown[row];
                _keyWasDown[row] = down;

                if (pressed && ctrl && Time.NormalTime - _lastPressTime >= PressCooldown)
                {
                    _lastPressTime = Time.NormalTime;
                    PressRow(row);
                }
            }

            bool endDown = active && IsDown(VK_END);
            bool endPressed = endDown && !_quitKeyWasDown;
            _quitKeyWasDown = endDown;
            if (endPressed && ctrl && Time.NormalTime - _lastPressTime >= PressCooldown)
            {
                _lastPressTime = Time.NormalTime;
                QuitEverywhere();
            }
        }

        /// <summary>Ctrl+End: /quit on every injected character, this one too.</summary>
        private void QuitEverywhere()
        {
            int pressId = _random.Next(1, int.MaxValue);
            StartPress(pressId, $"{QuitKeyName} pressed on {MyName}: /quit on all characters");
            _ipc.Broadcast(new TravelPressMessage { PressId = pressId, Row = QuitRow, Action = (int)TravelAction.Quit });
            Fire(QuitRow, pressId, false, TravelAction.Quit);
        }

        /// <summary>A travel key was pressed on this character: tell every other one, then fire here.</summary>
        /// <summary>
        /// A travel key was pressed on this character. If this character's travel nano for that row is
        /// running, the key cancels it on every character; otherwise every character travels.
        /// The pressing character decides, so all characters do the same thing.
        /// </summary>
        private void PressRow(int row)
        {
            TravelAction action = IsRowNanoRunning(row) ? TravelAction.Cancel : TravelAction.Use;
            int pressId = _random.Next(1, int.MaxValue);
            StartPress(pressId, $"{KeyNames[row]} pressed on {MyName}: {Describe(action)} on all characters");
            _ipc.Broadcast(new TravelPressMessage { PressId = pressId, Row = row, Action = (int)action });
            Fire(row, pressId, false, action);
        }

        /// <summary>/travel off or the window's button: stop every travel nano on every character.</summary>
        private void CancelAllEverywhere()
        {
            int pressId = _random.Next(1, int.MaxValue);
            StartPress(pressId, $"Cancel all pressed on {MyName}: stop travel nanos on all characters");
            _ipc.Broadcast(new TravelPressMessage { PressId = pressId, Row = -1, Action = (int)TravelAction.CancelAll });
            Fire(-1, pressId, false, TravelAction.CancelAll);
        }

        private void OnPressReceived(int senderId, IPCMessage message)
        {
            var msg = (TravelPressMessage)message;
            var action = (TravelAction)msg.Action;

            if (action == TravelAction.CancelAll)
            {
                StartPress(msg.PressId, $"Cancel all pressed on {NameOf(senderId)}: stop travel nanos on all characters");
                Fire(-1, msg.PressId, false, action);
                return;
            }

            if (action == TravelAction.Quit)
            {
                StartPress(msg.PressId, $"{QuitKeyName} pressed on {NameOf(senderId)}: /quit on all characters");
                Fire(QuitRow, msg.PressId, false, action);
                return;
            }

            if (msg.Row < 0 || msg.Row >= Rows)
                return;

            StartPress(msg.PressId, $"{KeyNames[msg.Row]} pressed on {NameOf(senderId)}: {Describe(action)} on all characters");
            Fire(msg.Row, msg.PressId, false, action);
        }

        private static string Describe(TravelAction action) =>
            action == TravelAction.Use ? "travel" : "cancel travel nano";

        private void OnResultReceived(int senderId, IPCMessage message)
        {
            var msg = (TravelResultMessage)message;
            if (msg.PressId != _lastPressId)
                return;
            AddResult($"{msg.Character}: {msg.Text}");
        }

        // ------------------------------------------------------------------ travelling

        private void Fire(int row, int pressId, bool testOnly, TravelAction action = TravelAction.Use)
        {
            bool ok;
            string text;

            if (action == TravelAction.Quit)
            {
                // Not a travel key, so the "Join in" box doesn't apply: every injected character quits.
                Report(row, pressId, testOnly, true, "quitting the game (/quit)");
                try { Game.Quit(); }
                catch (Exception e) { Chat.WriteLine($"[Travel] /quit failed: {e.Message}", ChatColor.Red); }
                return;
            }

            if (!testOnly && !_settings.JoinIn)
            {
                ok = false;
                text = "joining in is off - ignored";
            }
            else if (action == TravelAction.CancelAll)
            {
                ok = CancelAllRows(out text);
            }
            else if (action == TravelAction.Cancel)
            {
                ok = CancelRow(row, out text);
            }
            else
            {
                ok = TryTravel(row, pressId, testOnly, out text);
            }

            if (text == null)
                return; // waiting for the Travel bag's contents; reported once it has loaded

            Report(row, pressId, testOnly, ok, text);
        }

        private void Report(int row, int pressId, bool testOnly, bool ok, string text)
        {
            string label = row >= 0 && row < Rows ? KeyNames[row] : row == QuitRow ? QuitKeyName : "Cancel all";
            Chat.WriteLine($"[Travel] {label}: {text}", ok ? ChatColor.Green : ChatColor.Orange);

            if (pressId == _lastPressId)
                AddResult($"{MyName}: {text}");

            if (!testOnly)
            {
                _ipc.Broadcast(new TravelResultMessage
                {
                    PressId = pressId,
                    Row = row,
                    Ok = ok,
                    Character = Trim(MyName, 40),
                    Text = Trim(text, 200)
                });
            }
        }

        private bool TryTravel(int row, int pressId, bool testOnly, out string text)
        {
            TravelSlot slot = _settings.Slots[row];

            if (slot.Kind == SlotKind.Camp)
            {
                // 2.4: /camp - the game's own camp (sit, count down, log out to character select).
                try
                {
                    Game.Camp();
                    text = "camping (/camp) - logging out to character select";
                    return true;
                }
                catch (Exception e)
                {
                    text = $"/camp failed: {e.Message}";
                    return false;
                }
            }

            if (string.IsNullOrEmpty(slot.Name))
            {
                text = "nothing set - skipped";
                return false;
            }

            if (Game.IsZoning)
            {
                text = "zoning - skipped";
                return false;
            }

            if (slot.Kind == SlotKind.Nano)
            {
                List<Spell> spells = Spell.List.Where(x => x.Name == slot.Name).ToList();
                if (spells.Count == 0)
                {
                    text = $"'{slot.Name}' not in nano list - skipped";
                    return false;
                }

                // AO#'s requirement check (MeetsSelfUseReqs) wrongly rejects some nanos the character
                // can cast by hand (person's report: Phasefront Wraith), so it never blocks a cast. It
                // only picks the better version when a name appears more than once; the game itself
                // decides, and any refusal it gives is passed on through OnFeedback.
                Spell spell = spells.Count == 1 ? spells[0] : (spells.FirstOrDefault(SafeMeetsSelfUseReqs) ?? spells[0]);

                // Like the requirement check, "ready" and "nano cost" from AO# are only hints: they are
                // shown with the result but never stop the cast. The game has the final say.
                LocalPlayer me = DynelManager.LocalPlayer;
                string note = "";
                if (!SafeIsReady(spell))
                    note = " (AO# thinks it may not be ready)";
                else
                {
                    int cost = SafeCost(spell);
                    if (cost > 0 && me.Nano < cost)
                        note = $" (AO# thinks nano may be low: {me.Nano}/{cost})";
                }

                // Moving, sitting or another cast on its way: wait up to a few seconds, then try anyway.
                bool waited = _pendingNanoDue;
                _pendingNanoDue = false;
                if (!waited && (!me.MovementStatePermitsCasting || Spell.HasPendingCast))
                {
                    _pendingNano = new PendingFire { Row = row, PressId = pressId, TestOnly = testOnly, Deadline = Time.NormalTime + NanoWaitTimeout };
                    text = null; // cast as soon as the character stands still
                    return false;
                }

                if (RunningBuffs(row).Count > 0)
                {
                    text = $"'{slot.Name}' is already running (press {KeyNames[row]} again to cancel, or /travel off)";
                    return true;
                }

                StartBuffWatch(row, spell);
                CastOnSelf(spell);
                _feedbackUntil = Time.NormalTime + FeedbackWatchTime;
                _feedbackRow = row;
                _feedbackPressId = pressId;
                _feedbackTestOnly = testOnly;
                text = $"cast nano '{slot.Name}'{note}";
                return true;
            }

            Backpack bag = FindTravelBag();
            if (bag == null)
            {
                text = $"no bag named '{TravelBagName}' in inventory - skipped";
                return false;
            }

            List<Item> items = bag.Items;
            if (items.Count == 0)
            {
                // The game hasn't loaded this bag's contents yet: open it, then use the item once it has.
                if (RequestBagLoad(bag))
                {
                    _pending = new PendingFire { Row = row, PressId = pressId, TestOnly = testOnly, Deadline = Time.NormalTime + BagLoadTimeout };
                    Chat.WriteLine($"[Travel] Opening bag '{TravelBagName}' to load its contents...", ChatColor.LightBlue);
                    text = null;
                    return false;
                }

                text = $"could not load bag '{TravelBagName}' - open it by hand once";
                return false;
            }

            Item item = items.FirstOrDefault(x => x.Name == slot.Name);
            if (item == null)
            {
                text = $"'{slot.Name}' not in bag '{TravelBagName}' - skipped";
                return false;
            }

            item.Use();
            text = $"used '{slot.Name}'";
            return true;
        }

        private static Backpack FindTravelBag()
        {
            return Inventory.Backpacks.FirstOrDefault(b =>
                string.Equals((b.Name ?? "").Trim(), TravelBagName, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ cancelling travel nanos (2.0)
        // A travel nano stays on the character until it is removed. Travel removes it the way other AO#
        // plugins cancel buffs (aosp-bots GenericProfessionHandler.CancelBuffs / ManagerHelp):
        // Buff.Remove() on the character's own running buff. Items from the Travel bag are never touched.
        // A running buff belongs to a row when it has the row's nano id or name, or when it appeared
        // within a few seconds of Travel casting that row's nano and is in the same nano line
        // (some travel nanos put a differently numbered effect on the character).

        private const double BuffWatchTime = 6.0;

        private class BuffWatch
        {
            public int SpellId;
            public string Name;
            public NanoLine Line;
            public bool HasLine;
            public HashSet<int> Before = new HashSet<int>();
            public HashSet<int> Found = new HashSet<int>();
            public double Until;
        }

        private readonly BuffWatch[] _buffWatch = new BuffWatch[Rows];

        private static Buff[] MyBuffs()
        {
            try { return DynelManager.LocalPlayer?.Buffs ?? new Buff[0]; } catch { return new Buff[0]; }
        }

        private void StartBuffWatch(int row, Spell spell)
        {
            var watch = new BuffWatch { SpellId = spell.Id, Name = spell.Name, Until = Time.NormalTime + BuffWatchTime };
            try { watch.Line = spell.Nanoline; watch.HasLine = true; } catch { }
            foreach (Buff buff in MyBuffs())
                watch.Before.Add(buff.Id);

            // keep ids already learned for this row
            if (_buffWatch[row] != null && _buffWatch[row].Name == watch.Name)
                watch.Found.UnionWith(_buffWatch[row].Found);

            _buffWatch[row] = watch;
        }

        private void PollBuffWatch()
        {
            for (int row = 0; row < Rows; row++)
            {
                BuffWatch watch = _buffWatch[row];
                if (watch == null || Time.NormalTime > watch.Until)
                    continue;

                foreach (Buff buff in MyBuffs())
                {
                    if (watch.Before.Contains(buff.Id))
                        continue;
                    bool sameLine = false;
                    try { sameLine = watch.HasLine && buff.Nanoline == watch.Line; } catch { }
                    if (buff.Id == watch.SpellId || buff.Name == watch.Name || sameLine)
                        watch.Found.Add(buff.Id);
                }
            }
        }

        /// <summary>The character's running buffs that come from this row's travel nano (empty for item rows).</summary>
        private List<Buff> RunningBuffs(int row)
        {
            var result = new List<Buff>();
            if (row < 0 || row >= Rows)
                return result;

            TravelSlot slot = _settings.Slots[row];
            if (slot.Kind != SlotKind.Nano || string.IsNullOrEmpty(slot.Name))
                return result;

            var ids = new HashSet<int>();
            try { ids.UnionWith(Spell.List.Where(x => x.Name == slot.Name).Select(x => x.Id)); } catch { }
            BuffWatch watch = _buffWatch[row];
            if (watch != null && watch.Name == slot.Name)
                ids.UnionWith(watch.Found);

            foreach (Buff buff in MyBuffs())
                if (ids.Contains(buff.Id) || buff.Name == slot.Name)
                    result.Add(buff);

            return result;
        }

        private bool IsRowNanoRunning(int row) => RunningBuffs(row).Count > 0;

        private bool CancelRow(int row, out string text)
        {
            if (row < 0 || row >= Rows)
            {
                text = "unknown key";
                return false;
            }

            TravelSlot slot = _settings.Slots[row];
            if (slot.Kind != SlotKind.Nano)
            {
                text = $"{slot.Kind.ToString().ToLowerInvariant()} row - nothing to cancel";
                return false;
            }

            if (string.IsNullOrEmpty(slot.Name))
            {
                text = "nothing set - skipped";
                return false;
            }

            List<Buff> running = RunningBuffs(row);
            if (running.Count == 0)
            {
                text = $"'{slot.Name}' is not running - nothing to cancel";
                return false;
            }

            int removed = 0;
            foreach (Buff buff in running)
            {
                try
                {
                    if (buff.Remove())
                        removed++;
                }
                catch
                {
                }
            }

            text = removed > 0 ? $"cancelled nano '{slot.Name}'" : $"could not cancel '{slot.Name}' - right-click it in your buff list";
            return removed > 0;
        }

        private bool CancelAllRows(out string text)
        {
            var cancelled = new List<string>();
            var failed = new List<string>();
            var done = new HashSet<string>();

            for (int row = 0; row < Rows; row++)
            {
                TravelSlot slot = _settings.Slots[row];
                if (slot.Kind != SlotKind.Nano || string.IsNullOrEmpty(slot.Name) || done.Contains(slot.Name))
                    continue;
                done.Add(slot.Name);

                if (RunningBuffs(row).Count == 0)
                    continue;

                if (CancelRow(row, out _))
                    cancelled.Add(slot.Name);
                else
                    failed.Add(slot.Name);
            }

            if (cancelled.Count == 0 && failed.Count == 0)
            {
                text = "no travel nano running";
                return false;
            }

            text = cancelled.Count > 0 ? $"cancelled {string.Join(", ", cancelled.Select(x => $"'{x}'"))}" : "";
            if (failed.Count > 0)
                text += (text.Length > 0 ? "; " : "") + $"could not cancel {string.Join(", ", failed.Select(x => $"'{x}'"))}";
            return cancelled.Count > 0;
        }

        // ------------------------------------------------------------------ nano casting
        // Version 1.0 sent a raw "cast nano" network message (Spell.Cast with no target set), which the
        // game ignored. Travel now asks the game client to cast, exactly as clicking the nano in the
        // nano list does (n3EngineClientAnarchy_t::N3Msg_CastNanoSpell, nano first, then target),
        // targeting this character. If that function isn't available it falls back to Spell.Cast with
        // the character set as target, the way AO#'s CombatHandler casts.

        private const double NanoWaitTimeout = 3.0;    // wait this long for the character to stand still
        private const double FeedbackWatchTime = 2.0;  // game messages within this time are tied to the cast

        private PendingFire _pendingNano;
        private bool _pendingNanoDue;
        private double _feedbackUntil;
        private int _feedbackRow;
        private int _feedbackPressId;
        private bool _feedbackTestOnly;

        private static bool SafeMeetsSelfUseReqs(Spell spell)
        {
            try { return spell.MeetsSelfUseReqs(); } catch { return true; }
        }

        private static bool SafeIsReady(Spell spell)
        {
            try { return spell.IsReady; } catch { return true; }
        }

        private static int SafeCost(Spell spell)
        {
            try { return spell.Cost; } catch { return 0; }
        }

        private static void CastOnSelf(Spell spell)
        {
            Identity self = DynelManager.LocalPlayer.Identity;

            try
            {
                IntPtr pEngine = AOSharp.Common.Unmanaged.Imports.N3Engine_t.GetInstance();
                if (pEngine != IntPtr.Zero)
                {
                    Identity nano = new Identity(IdentityType.NanoProgram, spell.Id);
                    AOSharp.Common.Unmanaged.Imports.N3EngineClientAnarchy_t.CastNanoSpell(pEngine, ref nano, ref self);
                    return;
                }
            }
            catch
            {
                // fall through to the network cast below
            }

            spell.Cast(DynelManager.LocalPlayer, true);
        }

        private void PollPendingNano()
        {
            if (_pendingNano == null)
                return;

            LocalPlayer me = DynelManager.LocalPlayer;
            PendingFire p = _pendingNano;

            bool free = me != null && me.MovementStatePermitsCasting && !Spell.HasPendingCast;
            if (free || Time.NormalTime > p.Deadline)
            {
                // Standing still now, or waited long enough: cast (the game decides if it's allowed).
                _pendingNano = null;
                _pendingNanoDue = true;
                bool ok = TryTravel(p.Row, p.PressId, p.TestOnly, out string text);
                _pendingNanoDue = false;
                if (text != null)
                    Report(p.Row, p.PressId, p.TestOnly, ok, text);
            }
        }

        /// <summary>Pass on what the game says right after a Travel cast (e.g. why it failed) to the open Travel window.</summary>
        private void OnFeedback(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message) || Time.NormalTime > _feedbackUntil)
                    return;

                string text = $"game says: {message}";
                if (_feedbackPressId == _lastPressId)
                    AddResult($"{MyName}: {text}");

                if (!_feedbackTestOnly)
                {
                    _ipc.Broadcast(new TravelResultMessage
                    {
                        PressId = _feedbackPressId,
                        Row = _feedbackRow,
                        Ok = false,
                        Character = Trim(MyName, 40),
                        Text = Trim(text, 200)
                    });
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ Travel bag loading
        // AO only knows a backpack's contents after it has been opened. Like other AO# plugins
        // (aosp-bots "Crystal Filled by the Source": HandleBags uses the bag item), Travel opens the
        // bag by using the bag item itself: once shortly after inject/login, and again on demand if a
        // key press or the window finds it unloaded. When the contents have arrived it closes the bag.

        private const double BagLoadDelay = 2.0;      // seconds after inject before the first load
        private const double BagRetryInterval = 3.0;  // seconds between open attempts
        private const double BagLoadTimeout = 8.0;    // a waiting key press gives up after this
        private const double BagCloseDelay = 0.5;     // seconds after loading before closing the bag again
        private const int MaxBagAttempts = 3;         // open attempts per load request

        private class PendingFire
        {
            public int Row;
            public int PressId;
            public bool TestOnly;
            public double Deadline;
        }

        private PendingFire _pending;
        private double _startTime = -1;
        private bool _initialBagCheckDone;
        private bool _loadingBag;
        private int _bagAttempts;
        private double _bagRequestTime = -100;
        private bool _closeBagAfterLoad;
        private double _closeBagAt;
        private bool _refreshListsWhenLoaded;

        private static bool IsBagLoaded(Backpack bag) => bag != null && bag.Items.Count > 0;

        private static Item BagItem(Backpack bag) => Inventory.Items.FirstOrDefault(x => x.Slot == bag.Slot);

        /// <summary>Start (or keep) loading the Travel bag. Returns false when it can't be opened.</summary>
        private bool RequestBagLoad(Backpack bag)
        {
            if (bag == null)
                return false;

            if (!_loadingBag)
            {
                _loadingBag = true;
                _bagAttempts = 0;
                _bagRequestTime = -100;
            }

            if (Time.NormalTime - _bagRequestTime < BagRetryInterval)
                return true; // already asked a moment ago

            if (_bagAttempts >= MaxBagAttempts)
            {
                _loadingBag = false;
                return false;
            }

            Item bagItem = BagItem(bag);
            if (bagItem == null)
            {
                _loadingBag = false;
                return false;
            }

            _bagAttempts++;
            _bagRequestTime = Time.NormalTime;
            _closeBagAfterLoad = true;
            _closeBagAt = 0;
            bagItem.Use();
            return true;
        }

        private void PollTravelBag()
        {
            if (Game.IsZoning)
                return;

            if (_startTime < 0)
                _startTime = Time.NormalTime;

            bool initialDue = !_initialBagCheckDone && Time.NormalTime - _startTime >= BagLoadDelay;
            if (!initialDue && !_loadingBag && !_closeBagAfterLoad && _pending == null && !_refreshListsWhenLoaded)
                return;

            Backpack bag = FindTravelBag();

            if (initialDue)
            {
                _initialBagCheckDone = true;
                if (bag != null && !IsBagLoaded(bag))
                    RequestBagLoad(bag);
            }

            if (bag == null)
            {
                _loadingBag = false;
                _closeBagAfterLoad = false;
                _refreshListsWhenLoaded = false;
                FinishPending(false, $"no bag named '{TravelBagName}' in inventory - skipped");
                return;
            }

            if (!IsBagLoaded(bag))
            {
                if (_pending != null && Time.NormalTime > _pending.Deadline)
                {
                    _loadingBag = false;
                    FinishPending(false, $"could not load bag '{TravelBagName}' - open it by hand once");
                }
                else if (_loadingBag && !RequestBagLoad(bag))
                {
                    _refreshListsWhenLoaded = false;
                    FinishPending(false, $"could not load bag '{TravelBagName}' - open it by hand once");
                }
                return;
            }

            // The contents are in.
            _loadingBag = false;

            if (_closeBagAfterLoad)
            {
                if (_closeBagAt <= 0)
                {
                    _closeBagAt = Time.NormalTime + BagCloseDelay;
                }
                else if (Time.NormalTime >= _closeBagAt)
                {
                    _closeBagAfterLoad = false;
                    BagItem(bag)?.Use(); // close the bag window Travel opened
                }
            }

            if (_refreshListsWhenLoaded)
            {
                _refreshListsWhenLoaded = false;
                if (WindowOpen)
                    RefreshAllLists();
            }

            if (_pending != null)
            {
                PendingFire p = _pending;
                _pending = null;
                bool ok = TryTravel(p.Row, p.PressId, p.TestOnly, out string text);
                if (text != null)
                    Report(p.Row, p.PressId, p.TestOnly, ok, text);
            }
        }

        private void FinishPending(bool ok, string text)
        {
            if (_pending == null)
                return;
            PendingFire p = _pending;
            _pending = null;
            Report(p.Row, p.PressId, p.TestOnly, ok, text);
        }

        // ------------------------------------------------------------------ lists

        private List<string> NanoNames()
        {
            var all = new List<(string Name, bool Travel)>();
            foreach (Spell spell in Spell.List)
            {
                bool travel = false;
                try { travel = TravelNanoLines.Contains(spell.Nanoline); } catch { }
                all.Add((spell.Name, travel));
            }

            IEnumerable<(string Name, bool Travel)> query = all.Where(x => !string.IsNullOrEmpty(x.Name));
            if (!string.IsNullOrWhiteSpace(_filter))
                query = query.Where(x => x.Name.IndexOf(_filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

            return query
                .GroupBy(x => x.Name)
                .Select(g => (Name: g.Key, Travel: g.Any(x => x.Travel)))
                .OrderByDescending(x => x.Travel)
                .ThenBy(x => x.Name)
                .Select(x => x.Name)
                .Take(MaxListEntries)
                .ToList();
        }

        private static List<string> TravelBagItemNames()
        {
            Backpack bag = FindTravelBag();
            if (bag == null)
                return new List<string>();
            return bag.Items.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).Take(MaxListEntries).ToList();
        }

        // ------------------------------------------------------------------ window

        private bool WindowOpen => _window != null && _window.IsValid && _window.IsVisible;

        private const float DefaultWindowWidth = 600f;
        private const float DefaultWindowHeight = 700f;
        private const float MinWindowWidth = 460f;
        private const float MinWindowHeight = 560f;
        private const float MaxWindowSize = 2400f;
        private double _nextSizeCheck;

        private Vector2 SavedWindowSize()
        {
            float w = _settings.WindowWidth >= MinWindowWidth && _settings.WindowWidth <= MaxWindowSize ? _settings.WindowWidth : DefaultWindowWidth;
            float h = _settings.WindowHeight >= MinWindowHeight && _settings.WindowHeight <= MaxWindowSize ? _settings.WindowHeight : DefaultWindowHeight;
            return new Vector2(w, h);
        }

        /// <summary>Store the window's current size for this character (only sane values).</summary>
        private void RememberWindowSize()
        {
            try
            {
                if (_window == null || !_window.IsValid)
                    return;

                Rect frame = _window.GetFrame();
                float w = frame.MaxX - frame.MinX;
                float h = frame.MaxY - frame.MinY;
                if (w < MinWindowWidth || h < MinWindowHeight || w > MaxWindowSize || h > MaxWindowSize)
                    return;

                if (Math.Abs(w - _settings.WindowWidth) < 2 && Math.Abs(h - _settings.WindowHeight) < 2)
                    return;

                _settings.WindowWidth = w;
                _settings.WindowHeight = h;
                SaveSettings();
            }
            catch
            {
            }
        }

        private void ToggleWindow()
        {
            if (WindowOpen)
            {
                RememberWindowSize();
                _window.Close();
                _window = null;
                return;
            }

            string path = Path.Combine(PluginDirectory, "UI", "TravelWindow.xml");
            if (!File.Exists(path))
            {
                Chat.WriteLine($"[Travel] Window layout missing: {path}", ChatColor.Red);
                return;
            }

            // 2.0 used AO#'s default 300 x 300 window. 2.1 opens it larger, the way other AO# plugins
            // create their windows (AutoScale | NoFade), with a minimum size, and remembers the size
            // each character last dragged it to.
            Vector2 size = SavedWindowSize();
            _window = Window.CreateFromXml(WindowTitle, path, new Rect(50, 50, 50 + size.X, 50 + size.Y),
                WindowStyle.Default, WindowFlags.AutoScale | WindowFlags.NoFade);
            if (_window == null || !_window.IsValid)
            {
                Chat.WriteLine("[Travel] Could not open the Travel window.", ChatColor.Red);
                return;
            }

            // 2.3: the title bar reads "Travel <version>" (the window is created under that name, and the
            // title is set again in case the game shows the name differently).
            try { _window.SetTitle(WindowTitle); } catch { }

            try
            {
                _window.SetSizeLimits(new Vector2(MinWindowWidth, MinWindowHeight), new Vector2(MaxWindowSize, MaxWindowSize));
                _window.ResizeTo(size);
                _window.MoveToCenter();
            }
            catch
            {
                // sizing is cosmetic; the window still works at its default size
            }
            _nextSizeCheck = Time.NormalTime + 2.0;

            if (_window.FindView("CharLine", out TextView charLine))
                charLine.Text = $"Character: {MyName}  (these picks are for this character only)";

            if (_window.FindView("JoinIn", out Checkbox joinIn))
            {
                joinIn.SetValue(_settings.JoinIn);
                joinIn.Toggled = (s, value) => { _settings.JoinIn = value; SaveSettings(); };
            }

            if (_window.FindView("Filter", out TextInputView filter))
                filter.Text = _filter;

            if (_window.FindView("Refresh", out Button refresh))
                refresh.Clicked = (s, b) => RefreshAllLists();

            if (_window.FindView("CancelAll", out Button cancelAll))
                cancelAll.Clicked = (s, b) => CancelAllEverywhere();

            for (int row = 0; row < Rows; row++)
            {
                int r = row;
                _dropdowns[r] = null;
                _entries[r] = new List<string>();

                if (_window.FindView($"Kind{r}", out Button kind))
                    kind.Clicked = (s, b) => ToggleKind(r);
                if (_window.FindView($"Test{r}", out Button test))
                    test.Clicked = (s, b) => TestRow(r);
                if (_window.FindView($"Pick{r}", out DropdownMenu dropdown))
                    _dropdowns[r] = dropdown;
            }

            RefreshAllLists();
            UpdateLastPressView();
            _window.Show(true);
        }

        private void RefreshAllLists()
        {
            if (_window == null || !_window.IsValid)
                return;

            if (_window.FindView("Filter", out TextInputView filter))
                _filter = filter.Text ?? "";

            List<string> nanos = NanoNames();
            List<string> items = TravelBagItemNames();

            Backpack bag = FindTravelBag();
            if (bag != null && !IsBagLoaded(bag) && _settings.Slots.Any(x => x.Kind == SlotKind.Item) && RequestBagLoad(bag))
                _refreshListsWhenLoaded = true;

            for (int row = 0; row < Rows; row++)
                RebuildRow(row, ListFor(_settings.Slots[row].Kind, nanos, items));
        }

        private void RebuildRow(int row, List<string> names)
        {
            TravelSlot slot = _settings.Slots[row];

            if (_window.FindView($"Kind{row}", out Button kind))
                kind.SetLabel(slot.Kind.ToString());

            DropdownMenu dropdown = _dropdowns[row];
            if (dropdown != null)
            {
                for (int i = _entries[row].Count - 1; i >= 0; i--)
                    dropdown.DeleteItem((uint)i);

                var entries = new List<string>
                {
                    slot.Kind == SlotKind.Nano ? "- choose a nano -" : slot.Kind == SlotKind.Item ? $"- choose from bag '{TravelBagName}' -" : "/camp - log this character out",
                    "(clear this key)"
                };
                entries.AddRange(names);

                foreach (string entry in entries)
                    dropdown.AppendItem(entry);

                _entries[row] = entries;
                _lastSelection[row] = SafeSelection(dropdown);
            }

            UpdateSetLine(row);
        }

        private List<string> ListFor(SlotKind kind, List<string> nanos, List<string> items)
        {
            switch (kind)
            {
                case SlotKind.Nano: return nanos ?? NanoNames();
                case SlotKind.Item: return items ?? TravelBagItemNames();
                default: return new List<string>();
            }
        }

        private void ToggleKind(int row)
        {
            TravelSlot slot = _settings.Slots[row];
            slot.Kind = slot.Kind == SlotKind.Nano ? SlotKind.Item : slot.Kind == SlotKind.Item ? SlotKind.Camp : SlotKind.Nano;
            slot.Name = null;
            SaveSettings();
            if (slot.Kind == SlotKind.Item)
            {
                Backpack bag = FindTravelBag();
                if (bag != null && !IsBagLoaded(bag) && RequestBagLoad(bag))
                    _refreshListsWhenLoaded = true;
            }
            RebuildRow(row, ListFor(slot.Kind, null, null));
        }

        private void TestRow(int row)
        {
            int pressId = _random.Next(1, int.MaxValue);
            TravelAction action = IsRowNanoRunning(row) ? TravelAction.Cancel : TravelAction.Use;
            StartPress(pressId, $"Test {KeyNames[row]} on {MyName} only: {Describe(action)}");
            Fire(row, pressId, true, action);
        }

        private double _nextSetLineRefresh;

        private void PollWindow()
        {
            if (!WindowOpen)
                return;

            if (Time.NormalTime >= _nextSizeCheck)
            {
                _nextSizeCheck = Time.NormalTime + 2.0;
                RememberWindowSize();
            }

            if (Time.NormalTime >= _nextSetLineRefresh)
            {
                _nextSetLineRefresh = Time.NormalTime + 1.0;
                for (int r = 0; r < Rows; r++)
                    if (_settings.Slots[r].Kind == SlotKind.Nano)
                        UpdateSetLine(r);
            }

            for (int row = 0; row < Rows; row++)
            {
                DropdownMenu dropdown = _dropdowns[row];
                if (dropdown == null)
                    continue;

                uint selection = SafeSelection(dropdown);
                if (selection == _lastSelection[row])
                    continue;

                _lastSelection[row] = selection;
                List<string> entries = _entries[row];

                if (selection == 1)
                {
                    _settings.Slots[row].Name = null;
                    if (_settings.Slots[row].Kind == SlotKind.Camp)
                    {
                        // clearing a /camp key turns it back into an empty nano key
                        _settings.Slots[row].Kind = SlotKind.Nano;
                        SaveSettings();
                        RebuildRow(row, NanoNames());
                        continue;
                    }
                }
                else if (selection >= 2 && selection < entries.Count)
                    _settings.Slots[row].Name = entries[(int)selection];
                else
                    continue;

                SaveSettings();
                UpdateSetLine(row);
            }
        }

        private static uint SafeSelection(DropdownMenu dropdown)
        {
            try { return dropdown.GetSelection(); } catch { return 0; }
        }

        private void UpdateSetLine(int row)
        {
            if (_window == null || !_window.IsValid || !_window.FindView($"Set{row}", out TextView line))
                return;

            TravelSlot slot = _settings.Slots[row];
            if (slot.Kind == SlotKind.Camp)
            {
                line.Text = "Set: /camp (each character logs out to character select)";
                return;
            }

            if (string.IsNullOrEmpty(slot.Name))
            {
                line.Text = slot.Kind == SlotKind.Nano ? "Not set - pick a nano" : $"Not set - pick an item from bag '{TravelBagName}'";
                return;
            }

            if (slot.Kind == SlotKind.Item && !IsBagLoaded(FindTravelBag()))
            {
                line.Text = $"Set: {slot.Name} (item from bag '{TravelBagName}' - bag contents loading)";
                return;
            }

            bool found = slot.Kind == SlotKind.Nano
                ? Spell.List.Any(x => x.Name == slot.Name)
                : TravelBagItemNames().Contains(slot.Name);

            string where = slot.Kind == SlotKind.Nano ? "nano" : $"item from bag '{TravelBagName}'";
            string running = slot.Kind == SlotKind.Nano && IsRowNanoRunning(row) ? $" - RUNNING, press {KeyNames[row]} to cancel" : "";
            line.Text = found ? $"Set: {slot.Name} ({where}){running}" : $"Set: {slot.Name} - MISSING ({where})";
        }

        // ------------------------------------------------------------------ last key press

        private void StartPress(int pressId, string title)
        {
            _lastPressId = pressId;
            _lastPressTitle = title;
            _lastResults.Clear();
            UpdateLastPressView();
        }

        private void AddResult(string line)
        {
            if (_lastResults.Count < ResultLines)
                _lastResults.Add(line);
            UpdateLastPressView();
        }

        private void UpdateLastPressView()
        {
            if (_window == null || !_window.IsValid)
                return;

            if (_window.FindView("LastTitle", out TextView title))
                title.Text = _lastPressTitle;

            for (int i = 0; i < ResultLines; i++)
                if (_window.FindView($"Last{i}", out TextView line))
                    line.Text = i < _lastResults.Count ? _lastResults[i] : "";
        }

        // ------------------------------------------------------------------ settings
        // Stored the same way other AO# plugins do (e.g. ManagerGMI): one Config.json per character,
        // keyed by the character id, under %LOCALAPPDATA%\AOSharp\Travel\<character id>\ -
        // outside the plugin and repository folders, so it survives logins, rebuilds and updates.

        private void LoadSettings()
        {
            _config = Config.Load(Config.PathFor(Game.ClientInst), Game.ClientInst, Rows, out bool created);

            if (created)
                Chat.WriteLine($"[Travel] New settings file for {MyName}: {_config.FilePath}", ChatColor.LightBlue);
            else
                Chat.WriteLine($"[Travel] Settings loaded for {MyName} ({_settings.Slots.Count(x => !string.IsNullOrEmpty(x.Name))} of {Rows} keys set).", ChatColor.LightBlue);
        }

        private void SaveSettings()
        {
            if (_config == null)
                return;

            if (!_config.Save(out string error))
                Chat.WriteLine($"[Travel] Could not save settings to {_config.FilePath}: {error}", ChatColor.Red);
        }

        // ------------------------------------------------------------------ helpers

        private static string MyName => DynelManager.LocalPlayer?.Name ?? "This character";

        private static string NameOf(int charId)
        {
            try
            {
                SimpleChar player = DynelManager.Players.FirstOrDefault(p => p.Identity.Instance == charId);
                if (player != null)
                    return player.Name;
            }
            catch { }
            return "another character";
        }

        private static string Trim(string s, int max) => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max));
    }

    public enum SlotKind
    {
        Nano,
        Item,
        Camp    // 2.4: /camp
    }

    public class TravelSlot
    {
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public SlotKind Kind { get; set; } = SlotKind.Nano;
        public string Name { get; set; }
    }
}
