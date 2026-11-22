using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WuwaChecklist
{
    /// <summary>
    /// 深色弹出菜单。不用 ContextMenu 的原因很简单：
    /// 系统默认那套白底圆角阴影跟我们的半透明面板放一起像两个软件。
    /// 滑块也是自己画的，WPF 原生滑块是浅色主题，摆进来会扎眼。
    /// </summary>
    internal sealed class DarkMenu : Popup
    {
        private readonly StackPanel _panel;
        private readonly Border _box;

        public DarkMenu()
        {
            AllowsTransparency = true;
            PopupAnimation = PopupAnimation.Fade;
            StaysOpen = false;
            Placement = PlacementMode.MousePoint;

            _panel = new StackPanel { Width = 176, Margin = new Thickness(1) };

            _box = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(248, 22, 24, 27)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 108, 114, 118)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4),
                Child = _panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            Child = _box;
        }

        public void AddItem(string text, Action act, bool disabled = false, bool bold = false)
        {
            var row = Row(disabled);
            row.Child = Label(text, disabled ? Theme.Dim : Theme.Txt, bold);
            if (!disabled)
                row.MouseLeftButtonUp += (s, e) => { Close(); act(); e.Handled = true; };
            _panel.Children.Add(row);
        }

        public void AddCheck(string text, bool isChecked, Action act)
        {
            var row = Row(false);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 没勾也要有个方框，否则看不出这一行能点
            var boxRect = new Rect(0, 0, 9, 9);
            var frame = new Path
            {
                Data = PanelView.RRect(boxRect, 1.5, 1.5, 1.5, 1.5),
                Stroke = isChecked ? Theme.BrAcc : Theme.BrRing,
                StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(frame, 0);
            grid.Children.Add(frame);

            if (isChecked)
            {
                var mark = new Path
                {
                    Data = PanelView.CheckPath(new Rect(0, 0, 9, 9)),
                    Stroke = Theme.BrAcc,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(mark, 0);
                grid.Children.Add(mark);
            }

            var txt = Label(text, Theme.Txt, false);
            txt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(txt, 1);
            grid.Children.Add(txt);

            row.Child = grid;
            row.MouseLeftButtonUp += (s, e) => { Close(); act(); e.Handled = true; };
            _panel.Children.Add(row);
        }

        public void AddSeparator()
        {
            _panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromArgb(120, 72, 78, 82))
            });
        }

        public void AddHeader(string text)
        {
            var t = Label(text, Theme.Dim, false);
            t.FontSize = 10;
            t.Margin = new Thickness(8, 6, 8, 3);
            _panel.Children.Add(t);
        }

        /// <summary>菜单里塞一个滑杆。值实时回调，松手才写盘。</summary>
        public void AddSlider(string text, double min, double max, double value, Action<double> onChange, Action onCommit)
        {
            var host = new StackPanel { Margin = new Thickness(8, 2, 8, 6) };
            var cap = new TextBlock
            {
                Text = text + "  " + Math.Round(value * 100) + "%",
                Foreground = Theme.BrRTxt,
                FontFamily = Theme.Family,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 3)
            };

            // 自绘滑块：一条细轨 + 一个方块把，配色跟面板一套
            var track = new Canvas { Height = 16, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var rail = new Border
            {
                Height = 2,
                Width = 148,
                Background = Theme.BrBarBg,
                CornerRadius = new CornerRadius(1)
            };
            Canvas.SetLeft(rail, 0);
            Canvas.SetTop(rail, 7);
            var knob = new Border
            {
                Width = 4,
                Height = 12,
                CornerRadius = new CornerRadius(1),
                Background = Theme.BrAcc
            };
            Canvas.SetTop(knob, 2);
            track.Children.Add(rail);
            track.Children.Add(knob);

            double cur = value;
            Action<double> layout = v =>
            {
                cur = Math.Max(min, Math.Min(max, v));
                double frac = (cur - min) / (max - min);
                Canvas.SetLeft(knob, Math.Round(frac * 144));
                cap.Text = text + "  " + Math.Round(cur * 100) + "%";
            };
            layout(value);

            bool dragging = false;
            Action<Point> apply = p =>
            {
                double frac = Math.Max(0, Math.Min(1, p.X / 148.0));
                layout(min + frac * (max - min));
                onChange(cur);
            };

            track.MouseLeftButtonDown += (s, e) =>
            {
                dragging = true;
                track.CaptureMouse();
                apply(e.GetPosition(rail));
                e.Handled = true;
            };
            track.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                apply(e.GetPosition(rail));
                e.Handled = true;
            };
            track.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                track.ReleaseMouseCapture();
                onCommit();
                e.Handled = true;
            };

            host.Children.Add(cap);
            host.Children.Add(track);
            _panel.Children.Add(host);
        }

        private Border Row(bool disabled)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                Cursor = disabled ? Cursors.Arrow : Cursors.Hand
            };
            if (!disabled)
            {
                row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(90, 63, 224, 200));
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            }
            return row;
        }

        private static TextBlock Label(string text, Color c, bool bold)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Theme.Freeze(c),
                FontFamily = Theme.Family,
                FontSize = 11.5,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };
        }

        /// <summary>关掉之前先让这一下点击走完，不然事件会漏到下面的面板上。</summary>
        private void Close()
        {
            IsOpen = false;
        }

        public void ShowAt(UIElement anchor)
        {
            PlacementTarget = anchor;
            IsOpen = true;
        }
    }
}
