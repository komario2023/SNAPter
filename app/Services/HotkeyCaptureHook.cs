using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Snapter
{
    /// <summary>Low-level keyboard hook so PrintScreen (VK 0x2C) reaches hotkey assignment when WPF does not.</summary>
    internal sealed class HotkeyCaptureHook : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeydown = 0x0100;
        private const int WmSyskeydown = 0x0104;
        private const int VkSnapshot = 0x2C;
        private const int VkControl = 0x11;
        private const int VkShift = 0x10;
        private const int VkMenu = 0x12;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;
        private const uint LLKHF_UP = 0x80;
        private const uint ScanPrtSc = 0x37;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private IntPtr _settingsHwnd = IntPtr.Zero;
        private TextBox _box;
        private Action<string> _onCaptured;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public HotkeyCaptureHook()
        {
            _proc = HookCallback;
        }

        public void AttachSettingsWindow(IntPtr hwnd)
        {
            _settingsHwnd = hwnd;
            EnsureHook();
        }

        public void SetTarget(TextBox box, Action<string> onCaptured)
        {
            _box = box;
            _onCaptured = onCaptured;
            EnsureHook();
        }

        public void ClearTarget()
        {
            _box = null;
            _onCaptured = null;
        }

        private void EnsureHook()
        {
            if (_hook != IntPtr.Zero) return;
            string mod = Process.GetCurrentProcess().MainModule.ModuleName;
            IntPtr hMod = GetModuleHandle(mod);
            if (hMod == IntPtr.Zero) hMod = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, hMod, 0);
            if (_hook == IntPtr.Zero)
                Log.Write("hotkey capture hook failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            else
                Log.Write("hotkey capture hook ok");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _box != null && _onCaptured != null)
            {
                if (_settingsHwnd != IntPtr.Zero && GetForegroundWindow() != _settingsHwnd)
                    return CallNextHookEx(_hook, nCode, wParam, lParam);

                int msg = wParam.ToInt32();
                if (msg == WmKeydown || msg == WmSyskeydown)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    if ((data.flags & LLKHF_UP) != 0)
                        return CallNextHookEx(_hook, nCode, wParam, lParam);

                    if (IsPrintScreen(data.vkCode, data.scanCode))
                    {
                        string combo = FormatPrintScreenCombo();
                        TextBox b = _box;
                        Action<string> done = _onCaptured;
                        b.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                        {
                            if (_box != b) return;
                            done(combo);
                        }));
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool IsPrintScreen(uint vk, uint scanCode)
        {
            if (vk == VkSnapshot) return true;
            uint sc = scanCode & 0xFF;
            return sc == ScanPrtSc;
        }

        internal static string FormatPrintScreenCombo()
        {
            bool ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
            bool win = (GetAsyncKeyState(VkLwin) & 0x8000) != 0 || (GetAsyncKeyState(VkRwin) & 0x8000) != 0;
            string s = "";
            if (ctrl) s += "Ctrl+";
            if (shift) s += "Shift+";
            if (alt) s += "Alt+";
            if (win) s += "Win+";
            return s + "PrintScreen";
        }

        public void Dispose()
        {
            ClearTarget();
            _settingsHwnd = IntPtr.Zero;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
