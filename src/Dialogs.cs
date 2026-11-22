using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WuwaChecklist
{
    /// <summary>一个跟主界面同色系的极简输入框。系统 MessageBox 太白了。</summary>
    internal sealed class InputWindow : Window
    {
        public string Value { get; private set; }
        private readonly TextBox _box;

        public InputWindow(string prompt, string title, string def)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.Height;
            Width = 320;
            Title = title;

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(252, 20, 22, 25)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 110, 116, 120)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Theme.BrTxt,
                FontFamily = Theme.Family,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = Theme.BrSub,
                FontFamily = Theme.Family,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _box = new TextBox
            {
                Text = def ?? "",
                FontFamily = Theme.Family,
                FontSize = 12,
                Foreground = Theme.BrTxt,
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 33, 37)),
                BorderBrush = Theme.BrAccDim,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                CaretBrush = Theme.BrAcc,
                SelectionBrush = Theme.BrAccDim,
                MaxLength = 32
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Value = _box.Text.Trim(); DialogResult = true; }
                else if (e.Key == Key.Escape) DialogResult = false;
            };
            sp.Children.Add(_box);

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            btns.Children.Add(Btn("取消", false, () => DialogResult = false));
            btns.Children.Add(Btn("确定", true, () => { Value = _box.Text.Trim(); DialogResult = true; }));
            sp.Children.Add(btns);

            root.Child = sp;
            Content = root;

            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        }

        private static Button Btn(string text, bool primary, Action act)
        {
            var b = new Button
            {
                Content = text,
                FontFamily = Theme.Family,
                FontSize = 11.5,
                Foreground = primary ? Theme.BrCheckInk : Theme.BrRTxt,
                Background = primary ? Theme.BrAcc : Theme.Freeze(Color.FromArgb(255, 44, 48, 52)),
                BorderBrush = primary ? Theme.BrAcc : Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            b.Click += (s, e) => act();
            return b;
        }
    }

    /// <summary>快捷键设置。点一下格子，按下组合键就记下来。</summary>
    internal sealed class HotkeyWindow : Window
    {
        private readonly MainWindow _mw;
        private readonly Dictionary<string, string> _draft = new Dictionary<string, string>();
        private string _capturing;

        public HotkeyWindow(MainWindow mw)
        {
            _mw = mw;
            Title = "快捷键";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Width = 340;
            SizeToContent = SizeToContent.Height;

            var st = mw.Store.Settings;
            _draft["toggle"] = st.HkToggle;
            _draft["lock"] = st.HkLock;
            _draft["fold"] = st.HkFold;

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(252, 20, 22, 25)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 110, 116, 120)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "全局快捷键",
                Foreground = Theme.BrTxt,
                FontFamily = Theme.Family,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "点一下某一行，然后按你要的组合键。必须带 Ctrl 或 Alt。",
                Foreground = Theme.BrSub,
                FontFamily = Theme.Family,
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 10)
            });

            sp.Children.Add(KeyRow("显示 / 隐藏", "toggle"));
            sp.Children.Add(KeyRow("锁定（点击穿透）", "lock"));
            sp.Children.Add(KeyRow("折叠 / 展开", "fold"));

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            btns.Children.Add(Btn("恢复默认", false, () =>
            {
                _draft["toggle"] = "Ctrl+Alt+W";
                _draft["lock"] = "Ctrl+Alt+K";
                _draft["fold"] = "Ctrl+Alt+F";
                Rebuild();
            }));
            btns.Children.Add(Btn("取消", false, () => DialogResult = false));
            btns.Children.Add(Btn("保存", true, Save));
            sp.Children.Add(btns);

            root.Child = sp;
            Content = root;
            _sp = sp;

            PreviewKeyDown += OnKey;
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is Border) DragMove(); };
        }

        private readonly StackPanel _sp;
        private readonly List<Border> _rows = new List<Border>();

        private void Rebuild()
        {
            // 只换行里的文字，别重建整个面板（重建会把按钮也拆了）
            foreach (var r in _rows)
            {
                var tb = ((StackPanel)r.Child).Children[1] as TextBlock;
                string key = (string)r.Tag;
                if (tb != null) tb.Text = _draft[key];
            }
        }

        private Border KeyRow(string label, string key)
        {
            var grid = new StackPanel { Orientation = Orientation.Horizontal };
            grid.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Theme.BrRTxt,
                FontFamily = Theme.Family,
                FontSize = 11.5,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(new TextBlock
            {
                Text = _draft[key],
                Foreground = Theme.BrAcc,
                FontFamily = Theme.Family,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });

            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 3),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(190, 36, 40, 44)),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = grid,
                Cursor = Cursors.Hand,
                Tag = key
            };
            row.MouseLeftButtonDown += (s, e) =>
            {
                _capturing = key;
                foreach (var r in _rows) r.BorderBrush = Brushes.Transparent;
                row.BorderBrush = Theme.BrAcc;
                e.Handled = false;
            };
            _rows.Add(row);
            return row;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (_capturing == null) return;
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
                k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;

            if (k == Key.Escape) { _capturing = null; Rebuild(); return; }

            string g = HotkeyManager.Format(e);
            if (g == null)
            {
                MessageBox.Show(this, "这个键不太行，换个带 Ctrl 或 Alt 的。", "不行", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var kv in _draft)
                if (kv.Key != _capturing && kv.Value == g)
                {
                    MessageBox.Show(this, "这个组合已经给「" + kv.Key + "」用了。", "重复了", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

            _draft[_capturing] = g;
            _capturing = null;
            Rebuild();
            e.Handled = true;
        }

        private void Save()
        {
            var st = _mw.Store.Settings;
            st.HkToggle = _draft["toggle"];
            st.HkLock = _draft["lock"];
            st.HkFold = _draft["fold"];
            _mw.Store.Save();
            DialogResult = true;
            _mw.ApplyHotkeys();
        }

        private static Button Btn(string text, bool primary, Action act)
        {
            var b = new Button
            {
                Content = text,
                FontFamily = Theme.Family,
                FontSize = 11.5,
                Foreground = primary ? Theme.BrCheckInk : Theme.BrRTxt,
                Background = primary ? Theme.BrAcc : Theme.Freeze(Color.FromArgb(255, 44, 48, 52)),
                BorderBrush = primary ? Theme.BrAcc : Theme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            b.Click += (s, e) => act();
            return b;
        }
    }
}
