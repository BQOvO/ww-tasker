using System;
using System.Drawing;
using System.IO;
using System.Windows;

namespace WuwaChecklist
{
    /// <summary>
    /// 托盘图标。以前按 Ctrl+Alt+W 把面板藏了就只能靠记忆找回它，
    /// 万一忘了快捷键就等于程序消失。挂个托盘图标至少还有个地方点。
    /// 用的是 WinForms 的 NotifyIcon——WPF 自己那份要自己画整个菜单，不划算。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly MainWindow _mw;
        private System.Windows.Forms.NotifyIcon _ni;
        private System.Windows.Forms.ContextMenuStrip _menu;
        private Icon _icon;

        public TrayIcon(MainWindow mw)
        {
            _mw = mw;
        }

        public void Show()
        {
            if (_ni != null) return;

            _icon = LoadIcon();
            _menu = BuildMenu();

            _ni = new System.Windows.Forms.NotifyIcon
            {
                Text = "鸣潮 · 查漏补缺",
                Icon = _icon ?? SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = _menu
            };
            _ni.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    _mw.ToggleVisible();
            };
        }

        public void Hide()
        {
            if (_ni == null) return;
            _ni.Visible = false;
            _ni.Dispose();
            _ni = null;
            if (_menu != null) { _menu.Dispose(); _menu = null; }
            if (_icon != null) { _icon.Dispose(); _icon = null; }
        }

        public void Dispose()
        {
            Hide();
        }

        /// <summary>菜单里的文案跟着主窗口的状态走，每次弹出前重刷一遍。</summary>
        private void Refresh()
        {
            if (_menu == null) return;
            var toggle = _menu.Items[0] as System.Windows.Forms.ToolStripMenuItem;
            var lockItem = _menu.Items[1] as System.Windows.Forms.ToolStripMenuItem;
            var fold = _menu.Items[2] as System.Windows.Forms.ToolStripMenuItem;
            if (toggle != null) toggle.Text = _mw.IsVisible ? "隐藏面板" : "显示面板";
            if (lockItem != null) lockItem.Text = _mw.Locked ? "解锁（允许点击）" : "锁定（点击穿透）";
            if (fold != null) fold.Text = _mw.Folded ? "展开完整面板" : "折叠成细条";
        }

        private System.Windows.Forms.ContextMenuStrip BuildMenu()
        {
            var m = new System.Windows.Forms.ContextMenuStrip();
            m.Opening += (s, e) => Refresh();

            var toggle = new System.Windows.Forms.ToolStripMenuItem("显示面板");
            toggle.Click += (s, e) => _mw.ToggleVisible();
            m.Items.Add(toggle);

            var lockItem = new System.Windows.Forms.ToolStripMenuItem("锁定（点击穿透）");
            lockItem.Click += (s, e) => _mw.ToggleLock();
            m.Items.Add(lockItem);

            var fold = new System.Windows.Forms.ToolStripMenuItem("折叠成细条");
            fold.Click += (s, e) => _mw.SetFolded(!_mw.Folded);
            m.Items.Add(fold);

            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var upd = new System.Windows.Forms.ToolStripMenuItem("检查任务库更新…");
            upd.Click += (s, e) => _mw.CheckUpdate(manual: true);
            m.Items.Add(upd);

            var quit = new System.Windows.Forms.ToolStripMenuItem("退出");
            quit.Click += (s, e) => _mw.Close();
            m.Items.Add(quit);

            return m;
        }

        /// <summary>exe 里那份 app.ico 就是托盘图标，不用另存一份。</summary>
        private static Icon LoadIcon()
        {
            try
            {
                var res = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
                if (res == null) return null;
                using (var st = res.Stream)
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    ms.Position = 0;
                    // 小尺寸那份更像托盘该有的样子，别拿 256 的缩
                    return new Icon(ms, new System.Drawing.Size(32, 32));
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
