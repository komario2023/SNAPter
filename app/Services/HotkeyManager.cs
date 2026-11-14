using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Snapter
{
    /// <summary>Global hotkeys through RegisterHotKey, received by a hidden message-only window.</summary>
    internal sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly HwndSource _source;
        private readonly Dictionary<int, string> _actions = new Dictionary<int, string>();
        private int _nextId = 1;

        /// <summary>Raised with the action name of the pressed hotkey.</summary>
        public event Action<string> Pressed;

        public HotkeyManager()
        {
            HwndSourceParameters p = new HwndSourceParameters("SnapterHotkeys");
            p.ParentWindow = new IntPtr(-3); // HWND_MESSAGE: no visible window
            p.WindowStyle = 0;
            p.Width = 0;
            p.Height = 0;
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        public bool Register(string action, string keys, out string error)
        {
            error = null;
            uint mods, vk;
            if (!TryParse(keys, out mods, out vk))
            {
                error = "cannot parse '" + keys + "'";
                return false;
            }
            int id = _nextId++;
            if (!RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, vk))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            _actions[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (int id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                string action;
                if (_actions.TryGetValue(wParam.ToInt32(), out action))
                {
                    handled = true;
                    Action<string> h = Pressed;
                    if (h != null) h(action);
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }

        /// <summary>Parses "Ctrl+Shift+Alt+R", "Win+F9", "PrintScreen", "Ctrl+PrintScreen".</summary>
        public static bool TryParse(string keys, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(keys)) return false;
            string[] parts = keys.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) return false;
                string u = p.ToUpperInvariant();
                bool last = (i == parts.Length - 1);
                if (!last)
                {
                    if (u == "CTRL" || u == "CONTROL") mods |= MOD_CONTROL;
                    else if (u == "ALT") mods |= MOD_ALT;
                    else if (u == "SHIFT") mods |= MOD_SHIFT;
                    else if (u == "WIN" || u == "WINDOWS") mods |= MOD_WIN;
                    else return false;
                }
                else
                {
                    vk = KeyToVk(u);
                    if (vk == 0) return false;
                }
            }
            return vk != 0;
        }

        private static uint KeyToVk(string u)
        {
            if (u.Length == 1)
            {
                char c = u[0];
                if (c >= 'A' && c <= 'Z') return (uint)c;
                if (c >= '0' && c <= '9') return (uint)c;
            }
            if (u.Length >= 2 && u[0] == 'F')
            {
                int n;
                if (int.TryParse(u.Substring(1), out n) && n >= 1 && n <= 24) return (uint)(0x70 + n - 1);
            }
            switch (u)
            {
                case "PRINTSCREEN": case "PRTSC": case "PRINT": case "SNAPSHOT": return 0x2C;
                case "SPACE": return 0x20;
                case "ENTER": case "RETURN": return 0x0D;
                case "ESC": case "ESCAPE": return 0x1B;
                case "TAB": return 0x09;
                case "INSERT": return 0x2D;
                case "DELETE": case "DEL": return 0x2E;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                case "LEFT": return 0x25;
                case "UP": return 0x26;
                case "RIGHT": return 0x27;
                case "DOWN": return 0x28;
                case "PAUSE": return 0x13;
                case "SCROLLLOCK": return 0x91;
            }
            return 0;
        }
    }
}
