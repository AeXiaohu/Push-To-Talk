using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PushToTalk
{
    /// <summary>
    /// An immutable chord. All keys in a chord must be down; extra held keys are
    /// allowed. A collection of chords uses OR semantics (see HotkeyInput).
    /// Ctrl/Shift/Alt match either side, while LCtrl/RCtrl etc. are side-specific.
    /// Empty chords are deliberately invalid, so an empty binding list is silent.
    /// </summary>
    public sealed class HotkeyBinding
    {
        private readonly Keys[] keys;

        public HotkeyBinding(IEnumerable<Keys> chordKeys)
        {
            if (chordKeys == null) throw new ArgumentNullException("chordKeys");
            keys = HotkeyInput.Canonicalize(chordKeys);
            if (keys.Length == 0) throw new ArgumentException("A shortcut needs at least one supported key.", "chordKeys");
        }

        /// <summary>A defensive copy of the canonical virtual-key codes.</summary>
        public Keys[] VirtualKeys { get { return (Keys[])keys.Clone(); } }
        internal Keys[] KeyCodes { get { return keys; } }
        public int Count { get { return keys.Length; } }
        public int KeyCount { get { return keys.Length; } }
        public string DisplayName { get { return Serialize(); } }

        /// <summary>
        /// Stable, culture-independent format, e.g. "LCtrl+F2", "MouseX1",
        /// "Ctrl+Shift+Space". Use one serialized string per alternative binding.
        /// </summary>
        public string Serialize()
        {
            string[] names = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) names[i] = HotkeyInput.GetKeyName(keys[i]);
            return String.Join("+", names);
        }

        public override string ToString() { return DisplayName; }

        /// <summary>
        /// Pure evaluation, suitable for deterministic tests. For example,
        /// Parse("Ctrl+F2") matches { RControlKey, F2, A }. An LCtrl binding
        /// requires LControlKey and does not match RControlKey.
        /// </summary>
        public bool IsDown(ISet<Keys> pressedKeys)
        {
            if (pressedKeys == null) return false;
            for (int i = 0; i < keys.Length; i++)
                if (!HotkeyInput.IsKeyDown(keys[i], pressedKeys)) return false;
            return true;
        }

        public static HotkeyBinding Parse(string text)
        {
            HotkeyBinding binding;
            string error;
            if (!TryParse(text, out binding, out error)) throw new FormatException(error);
            return binding;
        }

        public static bool TryParse(string text, out HotkeyBinding binding)
        {
            string error;
            return TryParse(text, out binding, out error);
        }

        public static bool TryParse(string text, out HotkeyBinding binding, out string error)
        {
            binding = null;
            error = null;
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "A shortcut cannot be empty.";
                return false;
            }
            List<Keys> parsed = new List<Keys>();
            foreach (string part in text.Split('+'))
            {
                Keys key;
                if (!HotkeyInput.TryGetKey(part.Trim(), out key))
                {
                    error = "Unsupported shortcut key: " + part.Trim();
                    return false;
                }
                parsed.Add(key);
            }
            binding = new HotkeyBinding(parsed);
            return true;
        }
    }

    /// <summary>
    /// A precompiled, deduplicated set of physical keys for active bindings.
    /// Recreate when the binding list changes; reuse for every normal timer tick.
    /// </summary>
    public sealed class HotkeyPoller
    {
        private readonly Keys[] keys;
        internal HotkeyPoller(Keys[] keys) { this.keys = keys; }
        public int PolledKeyCount { get { return keys.Length; } }
        public HashSet<Keys> GetPressedKeys() { return HotkeyInput.ReadKeys(keys); }
    }

    /// <summary>
    /// Stateless native polling plus pure chord evaluation. Poll at 10-20 ms on
    /// the UI thread, taking ONE snapshot for all bindings. Compute the aggregate
    /// result before touching microphone state, so overlapping holds never mute.
    /// No hooks, background thread, key suppression, or automatic bindings.
    /// </summary>
    public static class HotkeyInput
    {
        private static readonly Dictionary<int, string> names = BuildNames();
        private static readonly Dictionary<string, Keys> codes = BuildCodes();
        private static readonly Keys[] physicalKeys = BuildPhysicalKeys();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        /// <summary>
        /// Read only the documented high (currently down) bit. The unreliable
        /// low (pressed since previous query) bit is never used. Generic modifier
        /// codes are omitted from the snapshot to avoid duplicate capture names.
        /// GetAsyncKeyState returns zero on inaccessible/inactive desktops, which
        /// causes all bindings to release. Mouse codes represent physical buttons.
        /// Source: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getasynckeystate
        /// </summary>
        public static HashSet<Keys> GetPressedKeys()
        {
            return ReadKeys(physicalKeys);
        }

        /// <summary>
        /// Poll only keys used by these bindings. Use CreatePoller once when
        /// bindings change to avoid recompiling the same key set on every tick.
        /// </summary>
        public static HashSet<Keys> GetPressedKeys(IEnumerable<HotkeyBinding> bindings)
        {
            return CreatePoller(bindings).GetPressedKeys();
        }

        public static HotkeyPoller CreatePoller(IEnumerable<HotkeyBinding> bindings)
        {
            HashSet<Keys> keys = new HashSet<Keys>();
            if (bindings != null)
                foreach (HotkeyBinding binding in bindings)
                    if (binding != null)
                        foreach (Keys key in binding.KeyCodes)
                        {
                            if (key == Keys.ControlKey) { keys.Add(Keys.LControlKey); keys.Add(Keys.RControlKey); }
                            else if (key == Keys.ShiftKey) { keys.Add(Keys.LShiftKey); keys.Add(Keys.RShiftKey); }
                            else if (key == Keys.Menu) { keys.Add(Keys.LMenu); keys.Add(Keys.RMenu); }
                            else keys.Add(key);
                        }
            Keys[] codes = new Keys[keys.Count];
            keys.CopyTo(codes);
            return new HotkeyPoller(codes);
        }

        internal static HashSet<Keys> ReadKeys(Keys[] keys)
        {
            HashSet<Keys> down = new HashSet<Keys>();
            for (int i = 0; i < keys.Length; i++)
            {
                Keys key = keys[i];
                if ((GetAsyncKeyState((int)key) & 0x8000) != 0) down.Add(key);
            }
            return down;
        }

        /// <summary>OR evaluation; null/empty collections never activate the mic.</summary>
        public static bool IsAnyBindingDown(IEnumerable<HotkeyBinding> bindings, ISet<Keys> pressedKeys)
        {
            if (bindings == null || pressedKeys == null) return false;
            foreach (HotkeyBinding binding in bindings)
                if (binding != null && binding.IsDown(pressedKeys)) return true;
            return false;
        }

        /// <summary>
        /// Build a chord from a captured snapshot, or null if it has no supported
        /// keys. Physical L/R modifiers are preserved. Left/right mouse clicks are
        /// intentionally ignored so clicks on the recording UI cannot be bound.
        /// Middle, X1, and X2 mouse buttons are supported.
        /// </summary>
        public static HotkeyBinding CaptureBinding(IEnumerable<Keys> pressedKeys)
        {
            if (pressedKeys == null) return null;
            Keys[] chord = Canonicalize(pressedKeys);
            return chord.Length == 0 ? null : new HotkeyBinding(chord);
        }

        public static bool IsBindableKey(Keys key)
        {
            int code = (int)key;
            return code > 0 && code < 255 && code != (int)Keys.LButton &&
                code != (int)Keys.RButton && names.ContainsKey(code);
        }

        /// <summary>
        /// Deduplicate, expand WinForms key-data modifier flags, remove generic
        /// modifiers when the same family has an explicit L/R code, and sort.
        /// Unrecognized virtual keys and left/right mouse clicks are ignored.
        /// </summary>
        public static Keys[] Canonicalize(IEnumerable<Keys> input)
        {
            HashSet<Keys> result = new HashSet<Keys>();
            if (input != null)
            {
                foreach (Keys raw in input)
                {
                    if ((raw & Keys.Control) != 0) result.Add(Keys.ControlKey);
                    if ((raw & Keys.Shift) != 0) result.Add(Keys.ShiftKey);
                    if ((raw & Keys.Alt) != 0) result.Add(Keys.Menu);
                    Keys key = raw & Keys.KeyCode;
                    if (IsBindableKey(key)) result.Add(key);
                }
            }
            RemoveRedundantGeneric(result, Keys.ControlKey, Keys.LControlKey, Keys.RControlKey);
            RemoveRedundantGeneric(result, Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey);
            RemoveRedundantGeneric(result, Keys.Menu, Keys.LMenu, Keys.RMenu);
            Keys[] array = new Keys[result.Count];
            result.CopyTo(array);
            Array.Sort(array, CompareKeys);
            return array;
        }

        public static string GetKeyName(Keys key)
        {
            string name;
            return names.TryGetValue((int)key, out name) ? name : key.ToString();
        }

        public static bool TryGetKey(string name, out Keys key)
        {
            key = Keys.None;
            return !String.IsNullOrWhiteSpace(name) && codes.TryGetValue(name.Trim(), out key);
        }

        internal static bool IsKeyDown(Keys key, ISet<Keys> down)
        {
            if (down.Contains(key)) return true;
            if (key == Keys.ControlKey)
                return down.Contains(Keys.LControlKey) || down.Contains(Keys.RControlKey) || down.Contains(Keys.Control);
            if (key == Keys.ShiftKey)
                return down.Contains(Keys.LShiftKey) || down.Contains(Keys.RShiftKey) || down.Contains(Keys.Shift);
            if (key == Keys.Menu)
                return down.Contains(Keys.LMenu) || down.Contains(Keys.RMenu) || down.Contains(Keys.Alt);
            return false;
        }

        private static void RemoveRedundantGeneric(HashSet<Keys> keys, Keys generic, Keys left, Keys right)
        {
            if (keys.Contains(left) || keys.Contains(right)) keys.Remove(generic);
        }

        private static int CompareKeys(Keys left, Keys right)
        {
            int group = SortGroup(left).CompareTo(SortGroup(right));
            return group != 0 ? group : ((int)left).CompareTo((int)right);
        }

        private static int SortGroup(Keys key)
        {
            if (key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey) return 0;
            if (key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey) return 1;
            if (key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu) return 2;
            if (key == Keys.LWin || key == Keys.RWin) return 3;
            return 4;
        }

        private static Dictionary<int, string> BuildNames()
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                int code = (int)key;
                if (code > 0 && code < 255 && code != (int)Keys.LButton && code != (int)Keys.RButton)
                    map[code] = key.ToString();
            }
            map[(int)Keys.MButton] = "MouseMiddle";
            map[(int)Keys.XButton1] = "MouseX1";
            map[(int)Keys.XButton2] = "MouseX2";
            map[(int)Keys.ControlKey] = "Ctrl";
            map[(int)Keys.LControlKey] = "LCtrl";
            map[(int)Keys.RControlKey] = "RCtrl";
            map[(int)Keys.ShiftKey] = "Shift";
            map[(int)Keys.LShiftKey] = "LShift";
            map[(int)Keys.RShiftKey] = "RShift";
            map[(int)Keys.Menu] = "Alt";
            map[(int)Keys.LMenu] = "LAlt";
            map[(int)Keys.RMenu] = "RAlt";
            map[(int)Keys.Return] = "Enter";
            map[(int)Keys.Back] = "Backspace";
            map[(int)Keys.Capital] = "CapsLock";
            map[(int)Keys.Prior] = "PageUp";
            map[(int)Keys.Next] = "PageDown";
            map[(int)Keys.Snapshot] = "PrintScreen";
            map[(int)Keys.Scroll] = "ScrollLock";
            map[(int)Keys.Oemplus] = "Plus";
            for (int i = 0; i < 10; i++) map[(int)Keys.D0 + i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return map;
        }

        private static Dictionary<string, Keys> BuildCodes()
        {
            Dictionary<string, Keys> map = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, string> entry in names) map[entry.Value] = (Keys)entry.Key;
            foreach (string enumName in Enum.GetNames(typeof(Keys)))
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), enumName);
                if (IsBindableKey(key)) map[enumName] = key;
            }
            map["Control"] = Keys.ControlKey;
            map["LeftCtrl"] = Keys.LControlKey;
            map["RightCtrl"] = Keys.RControlKey;
            map["LeftControl"] = Keys.LControlKey;
            map["RightControl"] = Keys.RControlKey;
            map["LeftShift"] = Keys.LShiftKey;
            map["RightShift"] = Keys.RShiftKey;
            map["LeftAlt"] = Keys.LMenu;
            map["RightAlt"] = Keys.RMenu;
            map["Middle"] = Keys.MButton;
            map["Mouse3"] = Keys.MButton;
            map["Mouse4"] = Keys.XButton1;
            map["Mouse5"] = Keys.XButton2;
            map["X1"] = Keys.XButton1;
            map["X2"] = Keys.XButton2;
            map["Esc"] = Keys.Escape;
            map["="] = Keys.Oemplus;
            return map;
        }

        private static Keys[] BuildPhysicalKeys()
        {
            List<Keys> list = new List<Keys>();
            foreach (int code in names.Keys)
            {
                if (code != (int)Keys.ControlKey && code != (int)Keys.ShiftKey && code != (int)Keys.Menu)
                    list.Add((Keys)code);
            }
            list.Sort(CompareKeys);
            return list.ToArray();
        }
    }
}
