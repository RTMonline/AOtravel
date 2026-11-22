// Travel plugin MOCKUP - shows only the in-game "Travel" window that opens
// with /travel, and how the five multibox travel hotkeys behave.
// Not the real plugin.
use macroquad::prelude::*;

const SW: f32 = 1280.0;
const SH: f32 = 720.0;
const WIN_W: f32 = 640.0;
const WIN_H: f32 = 486.0;
const KEYS: [KeyCode; 5] = [KeyCode::I, KeyCode::O, KeyCode::P, KeyCode::L, KeyCode::G];
const KEY_NAMES: [&str; 5] = ["Ctrl+I", "Ctrl+O", "Ctrl+P", "Ctrl+L", "Ctrl+G"];
const FLASH_TIME: f32 = 1.2;

fn c(r: u8, g: u8, b: u8, a: u8) -> Color {
    Color::from_rgba(r, g, b, a)
}
fn txt() -> Color {
    c(205, 222, 236, 255)
}
fn dim() -> Color {
    c(130, 150, 168, 255)
}
fn accent() -> Color {
    c(96, 204, 232, 255)
}
fn gold() -> Color {
    c(232, 192, 96, 255)
}
fn ok_col() -> Color {
    c(110, 220, 120, 255)
}
fn warn_col() -> Color {
    c(240, 170, 70, 255)
}
fn bad_col() -> Color {
    c(235, 90, 80, 255)
}
fn mock_col() -> Color {
    c(230, 120, 200, 255)
}

#[derive(Clone, Copy, PartialEq, Debug)]
enum Kind {
    Nano,
    Item,
}

#[derive(Clone)]
struct Slot {
    kind: Kind,
    choice: Option<String>,
}

fn slot(kind: Kind, choice: Option<&str>) -> Slot {
    Slot { kind, choice: choice.map(|s| s.to_string()) }
}

struct Toon {
    name: &'static str,
    nanos: Vec<&'static str>,
    bag: Vec<&'static str>,
    slots: Vec<Slot>,
    listening: bool,
    flash: f32,
}

impl Toon {
    fn list(&self, k: Kind) -> &Vec<&'static str> {
        match k {
            Kind::Nano => &self.nanos,
            Kind::Item => &self.bag,
        }
    }
    fn slot_found(&self, i: usize) -> bool {
        match &self.slots[i].choice {
            Some(n) => self.list(self.slots[i].kind).iter().any(|x| x == n),
            None => false,
        }
    }
}

// Slots are in key order: Ctrl+I, Ctrl+O, Ctrl+P, Ctrl+L, Ctrl+G
fn make_toons() -> Vec<Toon> {
    let mk = |name, nanos: Vec<&'static str>, bag: Vec<&'static str>, slots: Vec<Slot>| Toon {
        name,
        nanos,
        bag,
        slots,
        listening: true,
        flash: 0.0,
    };
    vec![
        mk(
            "Medibox",
            vec!["Return Beacon", "Swift Stride", "Group Recall", "Healing Pulse", "Calm Mind"],
            vec!["Portable Gate: Hub", "Recall Crystal"],
            vec![
                slot(Kind::Nano, Some("Swift Stride")),
                slot(Kind::Item, Some("Portable Gate: Hub")),
                slot(Kind::Item, Some("Recall Crystal")),
                slot(Kind::Nano, None),
                slot(Kind::Nano, Some("Return Beacon")),
            ],
        ),
        mk(
            "Gridrunner",
            vec!["Grid Jump", "Swift Stride", "Hidden Path", "Quick Hands"],
            vec!["Recall Crystal", "Portable Gate: Outpost", "Map Shard: North"],
            vec![
                slot(Kind::Nano, Some("Swift Stride")),
                slot(Kind::Item, Some("Portable Gate: Outpost")),
                slot(Kind::Item, Some("Recall Crystal")),
                slot(Kind::Item, None),
                slot(Kind::Nano, Some("Grid Jump")),
            ],
        ),
        mk(
            "Quietstep",
            vec!["Shadow Walk", "Swift Stride", "Sharp Eye"],
            vec!["Recall Crystal"],
            vec![
                slot(Kind::Nano, Some("Swift Stride")),
                // Used up: the item is no longer in the Travel bag
                slot(Kind::Item, Some("Portable Gate: Hub")),
                slot(Kind::Item, Some("Recall Crystal")),
                slot(Kind::Nano, None),
                slot(Kind::Nano, Some("Shadow Walk")),
            ],
        ),
        mk(
            "Tinkerbolt",
            vec!["Engineered Rift", "Swift Stride", "Spare Parts"],
            vec!["Portable Gate: Hub", "Recall Crystal", "Jump Pack"],
            vec![
                slot(Kind::Nano, Some("Swift Stride")),
                slot(Kind::Item, Some("Portable Gate: Hub")),
                slot(Kind::Nano, None),
                slot(Kind::Item, Some("Jump Pack")),
                slot(Kind::Nano, Some("Engineered Rift")),
            ],
        ),
    ]
}

// ---------- input (real or scripted) ----------
#[derive(Default, Clone)]
struct Inp {
    mouse: Vec2,
    click: bool,
    down: bool,
    release: bool,
    keys: Vec<KeyCode>,
    ctrl: bool,
    chars: Vec<char>,
}

fn real_input() -> Inp {
    let (mx, my) = mouse_position();
    let mut keys = vec![];
    for k in [
        KeyCode::Enter,
        KeyCode::KpEnter,
        KeyCode::Escape,
        KeyCode::Backspace,
        KeyCode::I,
        KeyCode::O,
        KeyCode::P,
        KeyCode::L,
        KeyCode::G,
    ] {
        if is_key_pressed(k) {
            keys.push(k);
        }
    }
    let ctrl = is_key_down(KeyCode::LeftControl) || is_key_down(KeyCode::RightControl);
    let mut chars = vec![];
    while let Some(ch) = get_char_pressed() {
        if !ch.is_control() && !ctrl {
            chars.push(ch);
        }
    }
    Inp {
        mouse: vec2(mx, my),
        click: is_mouse_button_pressed(MouseButton::Left),
        down: is_mouse_button_down(MouseButton::Left),
        release: is_mouse_button_released(MouseButton::Left),
        keys,
        ctrl,
        chars,
    }
}

// ---------- layout (relative to the window's top-left corner) ----------
fn title_rect(p: Vec2) -> Rect {
    Rect::new(p.x, p.y, WIN_W, 30.0)
}
fn close_rect(p: Vec2) -> Rect {
    Rect::new(p.x + WIN_W - 28.0, p.y + 6.0, 20.0, 18.0)
}
fn char_rect(p: Vec2) -> Rect {
    Rect::new(p.x + 96.0, p.y + 42.0, 190.0, 26.0)
}
fn check_rect(p: Vec2) -> Rect {
    Rect::new(p.x + 18.0, p.y + 84.0, 16.0, 16.0)
}
fn row_y(p: Vec2, i: usize) -> f32 {
    p.y + 140.0 + i as f32 * 44.0
}
fn key_rect(p: Vec2, i: usize) -> Rect {
    Rect::new(p.x + 18.0, row_y(p, i), 78.0, 34.0)
}
fn nano_rect(p: Vec2, i: usize) -> Rect {
    Rect::new(p.x + 106.0, row_y(p, i), 58.0, 34.0)
}
fn item_rect(p: Vec2, i: usize) -> Rect {
    Rect::new(p.x + 166.0, row_y(p, i), 58.0, 34.0)
}
fn combo_rect(p: Vec2, i: usize) -> Rect {
    Rect::new(p.x + 234.0, row_y(p, i), 310.0, 34.0)
}
fn test_rect(p: Vec2, i: usize) -> Rect {
    Rect::new(p.x + 554.0, row_y(p, i), 68.0, 34.0)
}
const DD_ROW: f32 = 22.0;

#[derive(Clone, Copy, PartialEq, Debug)]
enum Dd {
    Slot(usize),
    Char,
}

fn dd_anchor(p: Vec2, d: Dd) -> Rect {
    match d {
        Dd::Slot(i) => combo_rect(p, i),
        Dd::Char => char_rect(p),
    }
}
fn dropdown_box(p: Vec2, d: Dd, n: usize) -> Rect {
    let cb = dd_anchor(p, d);
    Rect::new(cb.x, cb.y + cb.h, cb.w, DD_ROW + n as f32 * DD_ROW + 4.0)
}
fn dropdown_entry(p: Vec2, d: Dd, n: usize, j: usize) -> Rect {
    let b = dropdown_box(p, d, n);
    Rect::new(b.x + 2.0, b.y + DD_ROW + j as f32 * DD_ROW, b.w - 4.0, DD_ROW)
}

struct LastPress {
    title: String,
    lines: Vec<(String, String, Color)>,
}

#[derive(Default)]
struct Stats {
    drew_rows: usize,
    drew_window: bool,
    drew_char_picker: bool,
    drew_hotkey_labels: usize,
    fires: usize,
    last_ok: Vec<bool>,
    last_targets: usize,
}

struct App {
    toons: Vec<Toon>,
    cur: usize,
    window_open: bool,
    win_pos: Vec2,
    cmd_buf: String,
    cmd_note: Option<String>,
    dropdown: Option<Dd>,
    drag: Option<Vec2>,
    hover: Vec2,
    last: Option<LastPress>,
    time: f32,
    stats: Stats,
}

impl App {
    fn new() -> Self {
        App {
            toons: make_toons(),
            cur: 0,
            window_open: false,
            win_pos: vec2((SW - WIN_W) / 2.0, (SH - WIN_H) / 2.0 + 8.0),
            cmd_buf: String::new(),
            cmd_note: None,
            dropdown: None,
            drag: None,
            hover: Vec2::ZERO,
            last: None,
            time: 0.0,
            stats: Stats::default(),
        }
    }

    fn entries(&self, d: Dd) -> Vec<String> {
        match d {
            Dd::Char => self.toons.iter().map(|t| t.name.to_string()).collect(),
            Dd::Slot(row) => {
                let t = &self.toons[self.cur];
                let mut v = vec!["(none - clear this key)".to_string()];
                for s in t.list(t.slots[row].kind) {
                    v.push(s.to_string());
                }
                v
            }
        }
    }

    fn update(&mut self, inp: &Inp, dt: f32) {
        self.time += dt;
        self.hover = inp.mouse;
        for t in self.toons.iter_mut() {
            t.flash = (t.flash - dt).max(0.0);
        }

        // Travel hotkeys work whether or not the window is open.
        if inp.ctrl {
            for (i, k) in KEYS.iter().enumerate() {
                if inp.keys.contains(k) {
                    self.fire(i, self.cur, false);
                }
            }
        }

        if !self.window_open {
            // Mockup stand-in for the game's chat line: type /travel + Enter.
            for ch in &inp.chars {
                if self.cmd_buf.len() < 40 {
                    self.cmd_buf.push(*ch);
                }
            }
            if inp.keys.contains(&KeyCode::Backspace) {
                self.cmd_buf.pop();
            }
            if inp.keys.contains(&KeyCode::Enter) || inp.keys.contains(&KeyCode::KpEnter) {
                let cmd = std::mem::take(&mut self.cmd_buf);
                self.submit(cmd.trim());
            }
        } else if inp.keys.contains(&KeyCode::Escape) {
            if self.dropdown.is_some() {
                self.dropdown = None;
            } else {
                self.window_open = false;
            }
        }

        if inp.click {
            self.click(inp.mouse);
        }
        if let Some(off) = self.drag {
            if inp.down {
                let np = inp.mouse - off;
                self.win_pos = vec2(np.x.clamp(0.0, SW - WIN_W), np.y.clamp(30.0, SH - 40.0));
            }
            if inp.release || !inp.down {
                self.drag = None;
            }
        }
    }

    fn submit(&mut self, cmd: &str) {
        if cmd.is_empty() {
            return;
        }
        if cmd.eq_ignore_ascii_case("/travel") {
            self.window_open = true;
            self.dropdown = None;
            self.cmd_note = None;
        } else {
            self.cmd_note = Some(format!("'{}' is not the plugin's command - type /travel", cmd));
        }
    }

    fn click(&mut self, m: Vec2) {
        if !self.window_open {
            return;
        }
        let p = self.win_pos;
        if let Some(d) = self.dropdown {
            let entries = self.entries(d);
            for j in 0..entries.len() {
                if dropdown_entry(p, d, entries.len(), j).contains(m) {
                    match d {
                        Dd::Char => self.cur = j,
                        Dd::Slot(row) => {
                            let t = &mut self.toons[self.cur];
                            t.slots[row].choice = if j == 0 { None } else { Some(entries[j].clone()) };
                        }
                    }
                    self.dropdown = None;
                    return;
                }
            }
            self.dropdown = None;
            if dd_anchor(p, d).contains(m) || dropdown_box(p, d, entries.len()).contains(m) {
                return;
            }
        }
        if !Rect::new(p.x, p.y, WIN_W, WIN_H).contains(m) {
            return;
        }
        if close_rect(p).contains(m) {
            self.window_open = false;
            return;
        }
        if title_rect(p).contains(m) {
            self.drag = Some(m - p);
            return;
        }
        if char_rect(p).contains(m) {
            self.dropdown = Some(Dd::Char);
            return;
        }
        let cr = check_rect(p);
        if Rect::new(cr.x, cr.y, 500.0, cr.h).contains(m) {
            let t = &mut self.toons[self.cur];
            t.listening = !t.listening;
            return;
        }
        let cur = self.cur;
        for i in 0..5 {
            for (r, k) in [(nano_rect(p, i), Kind::Nano), (item_rect(p, i), Kind::Item)] {
                if r.contains(m) {
                    let s = &mut self.toons[cur].slots[i];
                    if s.kind != k {
                        s.kind = k;
                        s.choice = None;
                    }
                    return;
                }
            }
            if combo_rect(p, i).contains(m) {
                self.dropdown = Some(Dd::Slot(i));
                return;
            }
            if test_rect(p, i).contains(m) {
                self.fire(i, cur, true);
                return;
            }
        }
    }

    /// A travel key was pressed on character `from`. In the real plugin the
    /// key press is sent to every injected character (no team needed); each
    /// one uses ITS OWN choice for that key.
    fn fire(&mut self, slot_i: usize, from: usize, test_only: bool) {
        let key = KEY_NAMES[slot_i];
        let targets: Vec<usize> = if test_only { vec![from] } else { (0..self.toons.len()).collect() };
        let mut lines = vec![];
        let mut oks = vec![];
        for i in targets {
            let found = self.toons[i].slot_found(slot_i);
            let t = &mut self.toons[i];
            let (msg, col, ok) = if !test_only && !t.listening {
                ("joining in is off - ignored".to_string(), dim(), false)
            } else {
                match t.slots[slot_i].choice.clone() {
                    None => ("nothing set - skipped".to_string(), warn_col(), false),
                    Some(n) if !found => {
                        let wh = if t.slots[slot_i].kind == Kind::Nano { "nano list" } else { "bag 'Travel'" };
                        (format!("'{}' not in {} - skipped", n, wh), bad_col(), false)
                    }
                    Some(n) => {
                        t.flash = FLASH_TIME;
                        if t.slots[slot_i].kind == Kind::Nano {
                            (format!("cast nano '{}'", n), ok_col(), true)
                        } else {
                            (format!("used '{}'", n), ok_col(), true)
                        }
                    }
                }
            };
            lines.push((t.name.to_string(), msg, col));
            oks.push(ok);
        }
        let used = oks.iter().filter(|b| **b).count();
        let title = if test_only {
            format!("Test {} on {} only", key, self.toons[from].name)
        } else {
            format!("{} pressed on {}: sent to {} characters, {} travelled", key, self.toons[from].name, oks.len(), used)
        };
        self.stats.fires += 1;
        self.stats.last_targets = oks.len();
        self.stats.last_ok = oks;
        self.last = Some(LastPress { title, lines });
    }

    // ---------------- drawing ----------------
    fn draw(&mut self) {
        clear_background(c(24, 26, 30, 255));
        self.draw_top_strip();
        if self.window_open {
            self.draw_window();
        } else {
            self.stats.drew_window = false;
            self.draw_closed();
        }
    }

    fn draw_top_strip(&self) {
        draw_rectangle(0.0, 0.0, SW, 26.0, c(46, 48, 54, 255));
        draw_text("MOCKUP - Travel plugin window only (not the real plugin)", 10.0, 18.0, 17.0, mock_col());
        let help = "Ctrl+I / O / P / L / G: travel on all characters    Esc: close window";
        let m = measure_text(help, None, 15, 1.0);
        draw_text(help, SW - m.width - 12.0, 18.0, 15.0, c(190, 190, 190, 255));
    }

    fn draw_closed(&self) {
        let r = Rect::new((SW - 560.0) / 2.0, SH / 2.0 - 70.0, 560.0, 140.0);
        draw_rectangle_lines(r.x, r.y, r.w, r.h, 1.0, c(70, 74, 82, 255));
        let l1 = "The plugin is injected and active - its window is hidden.";
        let l2 = "Type /travel and press Enter to open it on this character.";
        for (i, l) in [l1, l2].iter().enumerate() {
            let m = measure_text(l, None, 18, 1.0);
            draw_text(l, r.x + (r.w - m.width) / 2.0, r.y + 32.0 + i as f32 * 24.0, 18.0, if i == 0 { txt() } else { dim() });
        }
        let ir = Rect::new(r.x + 60.0, r.y + 84.0, r.w - 120.0, 26.0);
        draw_rectangle(ir.x, ir.y, ir.w, ir.h, c(10, 16, 26, 255));
        draw_rectangle_lines(ir.x, ir.y, ir.w, ir.h, 1.0, accent());
        let caret = if (self.time * 2.0) as i32 % 2 == 0 { "_" } else { "" };
        draw_text(&format!("> {}{}", self.cmd_buf, caret), ir.x + 8.0, ir.y + 18.0, 18.0, txt());
        if let Some(n) = &self.cmd_note {
            let m = measure_text(n, None, 15, 1.0);
            draw_text(n, r.x + (r.w - m.width) / 2.0, r.y + r.h + 22.0, 15.0, warn_col());
        }
        if let Some(lp) = &self.last {
            let m = measure_text(&lp.title, None, 15, 1.0);
            draw_text(&lp.title, r.x + (r.w - m.width) / 2.0, r.y + r.h + 46.0, 15.0, gold());
        }
    }

    fn draw_window(&mut self) {
        let cur = self.cur;
        let p = self.win_pos;
        let m = self.hover;
        let t = &self.toons[cur];
        // frame
        draw_rectangle(p.x + 6.0, p.y + 6.0, WIN_W, WIN_H, c(0, 0, 0, 110));
        draw_rectangle(p.x, p.y, WIN_W, WIN_H, c(14, 22, 34, 245));
        draw_rectangle_lines(p.x, p.y, WIN_W, WIN_H, 2.0, c(78, 120, 150, 255));
        // title bar
        let tr = title_rect(p);
        for i in 0..15 {
            let f = i as f32 / 15.0;
            draw_rectangle(tr.x + 1.0, tr.y + 1.0 + f * 28.0, tr.w - 2.0, 2.0, lerp_col(c(44, 78, 110, 255), c(20, 36, 56, 255), f));
        }
        draw_text("Travel", p.x + 12.0, p.y + 21.0, 21.0, txt());
        draw_text("multibox travel hotkeys", p.x + 82.0, p.y + 20.0, 15.0, dim());
        let cl = close_rect(p);
        draw_rectangle(cl.x, cl.y, cl.w, cl.h, if cl.contains(m) { c(170, 60, 50, 255) } else { c(70, 40, 40, 255) });
        draw_line(cl.x + 5.0, cl.y + 4.0, cl.x + cl.w - 5.0, cl.y + cl.h - 4.0, 2.0, txt());
        draw_line(cl.x + cl.w - 5.0, cl.y + 4.0, cl.x + 5.0, cl.y + cl.h - 4.0, 2.0, txt());

        // character line (picker is mockup-only; in-game this is the logged-in character)
        draw_text("Character:", p.x + 18.0, p.y + 60.0, 16.0, dim());
        let cr = char_rect(p);
        let open = self.dropdown == Some(Dd::Char);
        draw_rectangle(cr.x, cr.y, cr.w, cr.h, if cr.contains(m) || open { c(40, 26, 40, 255) } else { c(28, 18, 30, 255) });
        draw_rectangle_lines(cr.x, cr.y, cr.w, cr.h, 1.0, mock_col());
        draw_text(t.name, cr.x + 10.0, cr.y + 18.0, 17.0, accent());
        let ax = cr.x + cr.w - 16.0;
        draw_triangle(vec2(ax - 6.0, cr.y + 10.0), vec2(ax + 6.0, cr.y + 10.0), vec2(ax, cr.y + 17.0), txt());
        draw_text("mockup only", cr.x + cr.w + 8.0, cr.y + 18.0, 14.0, mock_col());
        draw_circle(p.x + 418.0, p.y + 55.0, 5.0, ok_col());
        draw_text(&format!("{} characters injected - no team needed", self.toons.len()), p.x + 428.0, p.y + 60.0, 14.0, txt());
        self.stats.drew_char_picker = true;

        // checkbox
        let ck = check_rect(p);
        draw_rectangle(ck.x, ck.y, ck.w, ck.h, c(10, 16, 26, 255));
        draw_rectangle_lines(ck.x, ck.y, ck.w, ck.h, 1.0, c(100, 140, 170, 255));
        if t.listening {
            draw_line(ck.x + 3.0, ck.y + 8.0, ck.x + 7.0, ck.y + 12.0, 2.0, ok_col());
            draw_line(ck.x + 7.0, ck.y + 12.0, ck.x + 13.0, ck.y + 3.0, 2.0, ok_col());
        }
        draw_text("Join in when a travel key is pressed on any of my characters", ck.x + 26.0, ck.y + 13.0, 16.0, txt());

        // header
        let hy = p.y + 130.0;
        draw_line(p.x + 12.0, hy - 18.0, p.x + WIN_W - 12.0, hy - 18.0, 1.0, c(50, 74, 96, 255));
        draw_text("Hotkey", p.x + 20.0, hy, 15.0, dim());
        draw_text("Source", p.x + 108.0, hy, 15.0, dim());
        draw_text("Travel option on this character", p.x + 236.0, hy, 15.0, dim());

        let mut drew_rows = 0;
        let mut keys_drawn = 0;
        for i in 0..5 {
            let s = &t.slots[i];
            let kr = key_rect(p, i);
            draw_rectangle(kr.x, kr.y, kr.w, kr.h, c(36, 30, 14, 255));
            draw_rectangle_lines(kr.x, kr.y, kr.w, kr.h, 1.0, gold());
            let km = measure_text(KEY_NAMES[i], None, 18, 1.0);
            draw_text(KEY_NAMES[i], kr.x + (kr.w - km.width) / 2.0, kr.y + 23.0, 18.0, gold());
            keys_drawn += 1;
            for (r, k, lbl) in [(nano_rect(p, i), Kind::Nano, "Nano"), (item_rect(p, i), Kind::Item, "Item")] {
                let on = s.kind == k;
                let bg = if on { c(36, 92, 120, 255) } else if r.contains(m) { c(30, 44, 60, 255) } else { c(20, 30, 42, 255) };
                draw_rectangle(r.x, r.y, r.w, r.h, bg);
                draw_rectangle_lines(r.x, r.y, r.w, r.h, 1.0, if on { accent() } else { c(60, 84, 104, 255) });
                let lm = measure_text(lbl, None, 16, 1.0);
                draw_text(lbl, r.x + (r.w - lm.width) / 2.0, r.y + 22.0, 16.0, if on { txt() } else { dim() });
            }
            let cb = combo_rect(p, i);
            let open = self.dropdown == Some(Dd::Slot(i));
            draw_rectangle(cb.x, cb.y, cb.w, cb.h, if cb.contains(m) || open { c(26, 40, 56, 255) } else { c(16, 24, 36, 255) });
            draw_rectangle_lines(cb.x, cb.y, cb.w, cb.h, 1.0, if open { accent() } else { c(70, 100, 124, 255) });
            let found = t.slot_found(i);
            match &s.choice {
                None => {
                    let hint = if s.kind == Kind::Nano { "- pick a nano -" } else { "- pick an item from 'Travel' -" };
                    draw_text(hint, cb.x + 26.0, cb.y + 22.0, 16.0, dim());
                    draw_circle(cb.x + 13.0, cb.y + 17.0, 4.0, warn_col());
                }
                Some(n) => {
                    draw_circle(cb.x + 13.0, cb.y + 17.0, 4.0, if found { ok_col() } else { bad_col() });
                    let label = if found { n.clone() } else { format!("{} (missing)", n) };
                    draw_text(&label, cb.x + 26.0, cb.y + 22.0, 17.0, if found { txt() } else { bad_col() });
                }
            }
            let ax = cb.x + cb.w - 18.0;
            let ay = cb.y + 14.0;
            draw_triangle(vec2(ax - 6.0, ay), vec2(ax + 6.0, ay), vec2(ax, ay + 7.0), txt());
            let tb = test_rect(p, i);
            draw_rectangle(tb.x, tb.y, tb.w, tb.h, if tb.contains(m) { c(50, 80, 60, 255) } else { c(30, 50, 40, 255) });
            draw_rectangle_lines(tb.x, tb.y, tb.w, tb.h, 1.0, c(100, 170, 120, 255));
            let tm = measure_text("Test", None, 16, 1.0);
            draw_text("Test", tb.x + (tb.w - tm.width) / 2.0, tb.y + 22.0, 16.0, txt());
            drew_rows += 1;
        }

        // last key press
        let ly = p.y + 366.0;
        draw_line(p.x + 12.0, ly - 12.0, p.x + WIN_W - 12.0, ly - 12.0, 1.0, c(50, 74, 96, 255));
        match &self.last {
            None => {
                draw_text("Last key press: none yet", p.x + 18.0, ly + 8.0, 15.0, dim());
            }
            Some(lp) => {
                draw_text(&lp.title, p.x + 18.0, ly + 8.0, 15.0, gold());
                for (j, (name, msg, col)) in lp.lines.iter().enumerate() {
                    let x = p.x + 18.0 + (j % 2) as f32 * 310.0;
                    let y = ly + 30.0 + (j / 2) as f32 * 20.0;
                    let fl = self.toons.iter().find(|t| t.name == name).map(|t| t.flash).unwrap_or(0.0);
                    let rad = 4.0 + 3.0 * (fl / FLASH_TIME);
                    draw_circle(x + 4.0, y - 5.0, rad, *col);
                    draw_text(name, x + 14.0, y, 15.0, txt());
                    draw_text(msg, x + 98.0, y, 14.0, *col);
                }
            }
        }

        // footer
        let fy = p.y + WIN_H - 26.0;
        draw_line(p.x + 12.0, fy - 12.0, p.x + WIN_W - 12.0, fy - 12.0, 1.0, c(50, 74, 96, 255));
        draw_text("A hotkey pressed on any character fires on all of them; each uses its own pick.", p.x + 18.0, fy + 2.0, 14.0, dim());
        draw_text("Nanos come from the nano list, items from the bag named 'Travel'. Saved per character.", p.x + 18.0, fy + 18.0, 14.0, dim());

        // dropdown on top
        if let Some(d) = self.dropdown {
            let entries = self.entries(d);
            let b = dropdown_box(p, d, entries.len());
            draw_rectangle(b.x + 4.0, b.y + 4.0, b.w, b.h, c(0, 0, 0, 120));
            draw_rectangle(b.x, b.y, b.w, b.h, c(10, 16, 26, 250));
            let (head, edge) = match d {
                Dd::Char => ("Mockup: view another character".to_string(), mock_col()),
                Dd::Slot(row) => (
                    if t.slots[row].kind == Kind::Nano {
                        format!("Nano list - {}", t.name)
                    } else {
                        format!("Inventory bag 'Travel' - {}", t.name)
                    },
                    accent(),
                ),
            };
            draw_rectangle_lines(b.x, b.y, b.w, b.h, 1.0, edge);
            draw_text(&head, b.x + 8.0, b.y + 16.0, 14.0, if d == Dd::Char { mock_col() } else { gold() });
            for (j, e) in entries.iter().enumerate() {
                let er = dropdown_entry(p, d, entries.len(), j);
                let sel = match d {
                    Dd::Char => j == cur,
                    Dd::Slot(row) => t.slots[row].choice.as_deref() == Some(e.as_str()),
                };
                if er.contains(m) {
                    draw_rectangle(er.x, er.y, er.w, er.h, c(36, 92, 120, 255));
                } else if sel {
                    draw_rectangle(er.x, er.y, er.w, er.h, c(24, 52, 70, 255));
                }
                let first_dim = matches!(d, Dd::Slot(_)) && j == 0;
                draw_text(e, er.x + 10.0, er.y + 16.0, 15.0, if first_dim { dim() } else { txt() });
            }
        }
        self.stats.drew_window = true;
        self.stats.drew_rows = drew_rows;
        self.stats.drew_hotkey_labels = keys_drawn;
    }
}

fn lerp_col(a: Color, b: Color, t: f32) -> Color {
    Color::new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t)
}

// ---------- window focus (checks must not steal focus) ----------
#[cfg(windows)]
mod win {
    use windows_sys::Win32::Foundation::{BOOL, HWND, LPARAM};
    use windows_sys::Win32::System::Threading::GetCurrentProcessId;
    use windows_sys::Win32::UI::WindowsAndMessaging::*;
    pub fn foreground() -> isize {
        unsafe { GetForegroundWindow() as isize }
    }
    unsafe extern "system" fn cb(h: HWND, lp: LPARAM) -> BOOL {
        let out = lp as *mut isize;
        let mut pid = 0u32;
        GetWindowThreadProcessId(h, &mut pid);
        if pid == GetCurrentProcessId() && IsWindowVisible(h) != 0 {
            *out = h as isize;
            return 0;
        }
        1
    }
    pub fn send_back(prev: isize) {
        unsafe {
            let mut me: isize = 0;
            EnumWindows(Some(cb), &mut me as *mut isize as LPARAM);
            if me != 0 {
                SetWindowPos(me as HWND, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            if prev != 0 && prev != me {
                SetForegroundWindow(prev as HWND);
            }
        }
    }
}
#[cfg(not(windows))]
mod win {
    pub fn foreground() -> isize {
        0
    }
    pub fn send_back(_prev: isize) {}
}

// ---------- self-test script ----------
fn click_at(r: Rect) -> Inp {
    Inp { mouse: vec2(r.x + 20.0, r.y + 10.0), click: true, down: true, ..Default::default() }
}

fn script_input(frame: u32, app: &App) -> Inp {
    let mut inp = Inp::default();
    let p = app.win_pos;
    let typed = "/travel";
    match frame {
        5..=11 => inp.chars.push(typed.chars().nth((frame - 5) as usize).unwrap()),
        13 => inp.keys.push(KeyCode::Enter),
        20 => {
            inp.mouse = vec2(p.x + 300.0, p.y + 12.0);
            inp.click = true;
            inp.down = true;
        }
        21..=40 => {
            inp.mouse = vec2(p.x + 300.0 - 6.0, p.y + 12.0 + 2.0);
            inp.down = true;
        }
        41 => {
            inp.mouse = vec2(p.x + 300.0, p.y + 12.0);
            inp.release = true;
        }
        45 => return click_at(combo_rect(p, 3)),
        48 => {
            let n = app.entries(Dd::Slot(3)).len();
            return click_at(dropdown_entry(p, Dd::Slot(3), n, 1));
        }
        55 => {
            inp.ctrl = true;
            inp.keys.push(KeyCode::L);
        }
        60 => return click_at(char_rect(p)),
        63 => {
            let n = app.entries(Dd::Char).len();
            return click_at(dropdown_entry(p, Dd::Char, n, 2));
        }
        70 => {
            inp.ctrl = true;
            inp.keys.push(KeyCode::G);
        }
        _ => {}
    }
    inp
}

fn conf() -> Conf {
    Conf {
        window_title: "Travel plugin - window mockup".to_string(),
        window_width: SW as i32,
        window_height: SH as i32,
        window_resizable: false,
        high_dpi: false,
        sample_count: 4,
        ..Default::default()
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let mut shot: Option<String> = None;
    let mut test: Option<String> = None;
    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--screenshot" if i + 1 < args.len() => {
                shot = Some(args[i + 1].clone());
                i += 1;
            }
            "--self-test" if i + 1 < args.len() => {
                test = Some(args[i + 1].clone());
                i += 1;
            }
            _ => {}
        }
        i += 1;
    }
    let background = shot.is_some() || test.is_some();
    let prev = if background { win::foreground() } else { 0 };
    macroquad::Window::from_config(conf(), run(shot, test, prev));
}

async fn run(shot: Option<String>, test: Option<String>, prev: isize) {
    let background = shot.is_some() || test.is_some();
    let mut app = App::new();
    let mut frame: u32 = 0;
    let mut win_start = Vec2::ZERO;
    let mut moved = 0.0f32;
    let mut opened_before = true;
    let mut opened = false;
    let mut assigned: Option<String> = None;
    let mut l_targets = 0usize;
    let mut l_used = 0usize;
    loop {
        frame += 1;
        if background && (frame == 1 || frame == 3) {
            win::send_back(prev);
        }
        let dt = if background { 1.0 / 60.0 } else { get_frame_time().min(0.1) };
        let inp = if test.is_some() {
            script_input(frame, &app)
        } else if shot.is_some() {
            let mut inp = Inp { mouse: vec2(-100.0, -100.0), ..Default::default() };
            if frame == 2 {
                app.submit("/travel");
            }
            if frame == 70 {
                inp.ctrl = true;
                inp.keys.push(KeyCode::G);
            }
            inp
        } else {
            real_input()
        };
        if test.is_some() && frame == 4 {
            opened_before = app.window_open;
        }
        app.update(&inp, dt);

        if test.is_some() {
            if frame == 14 {
                opened = app.window_open;
                win_start = app.win_pos;
            }
            if frame == 42 {
                moved = app.win_pos.distance(win_start);
            }
            if frame == 50 {
                assigned = app.toons[0].slots[3].choice.clone();
            }
            if frame == 56 {
                l_targets = app.stats.last_targets;
                l_used = app.stats.last_ok.iter().filter(|b| **b).count();
            }
        }

        app.draw();

        if let Some(path) = &shot {
            if frame >= 120 {
                let mut img = get_screen_data();
                for px in img.bytes.chunks_mut(4) {
                    px[3] = 255;
                }
                img.export_png(path);
                std::process::exit(0);
            }
        }
        if let Some(path) = &test {
            if frame >= 90 {
                let s = &app.stats;
                let g_used = s.last_ok.iter().filter(|b| **b).count();
                let switched = app.cur == 2;
                let move_ok = !opened_before && opened && moved > 50.0 && switched;
                let act_ok = assigned.is_some() && l_targets == 4 && l_used >= 1 && s.fires >= 2 && s.last_targets == 4 && g_used >= 1;
                let hud_ok = s.drew_window && s.drew_char_picker && s.drew_hotkey_labels == 5 && s.drew_rows == 5;
                let json = format!(
                    "{{\"version\": 1, \"steps\": [\
{{\"step\": \"move\", \"passed\": {}, \"detail\": \"window hidden after inject: {}, typed /travel (window open: {}), dragged the Travel window {:.0} px, picked character 3 in the mockup picker (showing: {})\"}}, \
{{\"step\": \"act\", \"passed\": {}, \"detail\": \"set Ctrl+L to {} on character 1, pressed Ctrl+L: sent to {} characters, {} travelled; pressed Ctrl+G: sent to {} characters, {} travelled\"}}, \
{{\"step\": \"hud\", \"passed\": {}, \"detail\": \"Travel window drawn {}, character picker {}, {} hotkey rows with {} key labels (Ctrl+I, O, P, L, G)\"}}]}}",
                    move_ok,
                    !opened_before,
                    opened,
                    moved,
                    app.toons[app.cur].name,
                    act_ok,
                    assigned.clone().unwrap_or_else(|| "nothing".into()).replace('\'', ""),
                    l_targets,
                    l_used,
                    s.last_targets,
                    g_used,
                    hud_ok,
                    s.drew_window,
                    s.drew_char_picker,
                    s.drew_rows,
                    s.drew_hotkey_labels
                );
                let _ = std::fs::write(path, json);
                std::process::exit(0);
            }
        }
        next_frame().await;
    }
}
