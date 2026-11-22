using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WuwaChecklist
{
    /// <summary>
    /// 全局快捷键。用 RegisterHotKey 挂在窗口句柄上，不装钩子、不注入。
    /// </summary>
    internal sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int IdToggle = 9001;
        public const int IdLock = 9002;
        public const int IdFold = 9003;

        private readonly Dictionary<int, string> _registered = new Dictionary<int, string>();
        private HwndSource _source;
        private IntPtr _hwnd = IntPtr.Zero;

        /// <summary>三个动作直接做成事件，主窗口订阅就行。</summary>
        public event Action<object, EventArgs> Toggle;
        public event Action<object, EventArgs> Lock;
        public event Action<object, EventArgs> Fold;

        public HotkeyManager(IntPtr hwnd)
        {
            _hwnd = hwnd;
            if (hwnd != IntPtr.Zero)
            {
                _source = HwndSource.FromHwnd(hwnd);
                if (_source != null) _source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                Action<object, EventArgs> h = null;
                if (id == IdToggle) h = Toggle;
                else if (id == IdLock) h = Lock;
                else if (id == IdFold) h = Fold;
                if (h != null) { h(this, EventArgs.Empty); handled = true; }
            }
            return IntPtr.Zero;
        }

        /// <summary>注册一组快捷键。返回失败的说明（成功返回 null）。</summary>
        public string Register(int id, string gesture)
        {
            Unregister(id);
            if (_hwnd == IntPtr.Zero) return "窗口句柄还没准备好";

            uint mods, vk;
            string why;
            if (!TryParse(gesture, out mods, out vk, out why)) return why;

            if (!RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 1409) return "已经被别的程序占用了";
                return "注册失败（错误码 " + err + "）";
            }
            _registered[id] = gesture;
            return null;
        }

        public void Unregister(int id)
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_registered.ContainsKey(id))
            {
                UnregisterHotKey(_hwnd, id);
                _registered.Remove(id);
            }
        }

        public void UnregisterAll()
        {
            var ids = new List<int>(_registered.Keys);
            foreach (var id in ids) Unregister(id);
        }

        public static bool IsValid(string gesture)
        {
            uint mods, vk;
            string why;
            return TryParse(gesture, out mods, out vk, out why);
        }

        /// <summary>解析 "Ctrl+Alt+W" 这种写法。</summary>
        public static bool TryParse(string gesture, out uint mods, out uint vk, out string error)
        {
            mods = 0;
            vk = 0;
            error = null;

            if (string.IsNullOrEmpty(gesture) || gesture.Trim().Length == 0)
            {
                error = "快捷键是空的";
                return false;
            }

            string[] parts = gesture.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            string key = null;

            foreach (var raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;

                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    mods |= MOD_CONTROL;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
                else
                {
                    if (key != null)
                    {
                        error = "只能有一个主键";
                        return false;
                    }
                    key = p.ToUpperInvariant();
                }
            }

            if (key == null)
            {
                error = "缺少主键（比如 W）";
                return false;
            }

            if (mods == 0)
            {
                error = "至少需要一个修饰键（Ctrl / Alt / Shift / Win）";
                return false;
            }

            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'A' && c <= 'Z') { vk = (uint)c; return true; }
                if (c >= '0' && c <= '9') { vk = (uint)c; return true; }
            }

            if (key.Length >= 2 && key[0] == 'F')
            {
                int n;
                if (int.TryParse(key.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + n - 1);
                    return true;
                }
            }

            if (key.Length == 1)
            {
                char c = key[0];
                if (c == ',' || c == '.' || c == '/' || c == ';' || c == '\'' || c == '[' || c == ']' || c == '\\' || c == '-' || c == '=')
                {
                    vk = (uint)c;
                    return true;
                }
                if (c == ' ') { vk = 0x20; return true; }
            }

            error = "这个主键本小姐不认（只支持 A-Z、0-9、F1-F24）";
            return false;
        }

        /// <summary>把 Windows 的按键事件格式化成 "Ctrl+Alt+W"，给设置界面用。</summary>
        public static string Format(System.Windows.Input.KeyEventArgs e)
        {
            var mods = System.Windows.Input.Keyboard.Modifiers;
            if (mods == System.Windows.Input.ModifierKeys.None) return null;

            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl) return null;
            if (key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt) return null;
            if (key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift) return null;
            if (key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin) return null;
            if (key == System.Windows.Input.Key.None) return null;

            string main = null;
            if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
                main = key.ToString();
            else if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
                main = ((int)(key - System.Windows.Input.Key.D0)).ToString();
            else if (key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F24)
                main = key.ToString();

            if (main == null) return null;

            string s = "";
            if ((mods & System.Windows.Input.ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((mods & System.Windows.Input.ModifierKeys.Alt) != 0) s += "Alt+";
            if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0) s += "Shift+";
            if ((mods & System.Windows.Input.ModifierKeys.Windows) != 0) s += "Win+";
            return s + main;
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
            _hwnd = IntPtr.Zero;
        }
    }
}
