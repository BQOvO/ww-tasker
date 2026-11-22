using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WuwaChecklist
{
    /// <summary>
    /// 折叠后的细条。整个面板缩成一根 236×30 的条，只报当前那一项进度。
    /// 面积小了，反而不该更透——扫一眼就要读到数。
    /// </summary>
    internal sealed class StripeView : FrameworkElement
    {
        private readonly MainWindow _host;
        private double _dpi = 1;
        private Rect _hover = Rect.Empty;

        private const double BarH = 30;

        public StripeView(MainWindow host)
        {
            _host = host;
            ClipToBounds = true;
            Focusable = false;
        }

        private Rect BtnUnfold(Rect r) { return new Rect(r.Right - 46, 0, 22, BarH); }
        private Rect BtnHide(Rect r) { return new Rect(r.Right - 24, 0, 24, BarH); }

        protected override void OnRender(DrawingContext dc)
        {
            _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 10) return;

            double op = Math.Min(1.0, _host.Store.Settings.Opacity + 0.15);

            var box = new Rect(0.5, 0.5, w - 1, h - 1);
            dc.DrawGeometry(Theme.Scaled(Theme.Bg, op),
                new Pen(Theme.Scaled(Theme.Border, Math.Min(1.0, op + 0.2)), 1),
                PanelView.RRect(box, 5, 5, 5, 5));

            // 左侧青色竖条 —— 折叠状态下唯一还在「报身份」的东西
            dc.PushClip(PanelView.RRect(box, 5, 5, 5, 5));
            dc.DrawRectangle(Theme.BrAcc, null, new Rect(0, 0, 3, h));
            dc.Pop();

            var region = _host.CurrentRegion;
            string name = region == null ? "—" : region.Name;
            int done = 0, total = 0;
            if (region != null) _host.CountRegion(region, out done, out total);

            var nm = Text(name, 12, Theme.BrTxt, true, 74);
            dc.DrawText(nm, new Point(11, (h - nm.Height) / 2 - 0.5));

            var cnt = Text(done + "/" + total, 10.5, Theme.BrAcc);
            dc.DrawText(cnt, new Point(11 + 78, (h - cnt.Height) / 2));

            // 中间那根进度线
            double bx = 11 + 78 + cnt.Width + 8;
            double bw = Math.Max(20, BtnUnfold(box).Left - 8 - bx);
            double by = h / 2 - 1;
            dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op), null, new Rect(bx, by, bw, 2));
            double frac = total <= 0 ? 0 : (double)done / total;
            if (frac > 0) dc.DrawRectangle(Theme.BrAcc, null, new Rect(bx, by, Math.Max(1, bw * frac), 2));

            // 展开
            var u = BtnUnfold(box);
            DrawUnfold(dc, new Point(u.Left + u.Width / 2, h / 2), u.Contains(_hover) ? Theme.BrAcc : Theme.BrRTxt);

            // 隐藏
            var hd = BtnHide(box);
            DrawCross(dc, new Point(hd.Left + hd.Width / 2, h / 2), hd.Contains(_hover) ? Theme.BrAcc : Theme.BrSub);

            // 拖动提示：整条都能拖，但鼠标上去给个抓手
            if (_hover != Rect.Empty) dc.DrawLine(new Pen(Theme.BrAccDim, 1), new Point(3, h - 1.5), new Point(w - 3, h - 1.5));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            var box = new Rect(0, 0, ActualWidth, ActualHeight);
            Rect r;
            if (BtnUnfold(box).Contains(p)) r = BtnUnfold(box);
            else if (BtnHide(box).Contains(p)) r = BtnHide(box);
            else r = box;
            if (r != _hover) { _hover = r; InvalidateVisual(); }
            Cursor = r == box ? Cursors.SizeAll : Cursors.Hand;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != Rect.Empty) { _hover = Rect.Empty; InvalidateVisual(); }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            var box = new Rect(0, 0, ActualWidth, ActualHeight);

            if (BtnUnfold(box).Contains(p)) { _host.SetFolded(false); e.Handled = true; return; }
            if (BtnHide(box).Contains(p)) { _host.HidePanel(); e.Handled = true; return; }

            _host.BeginDrag();
            e.Handled = true;
        }

        private void DrawUnfold(DrawingContext dc, Point c, Brush br)
        {
            // 一个朝右上的小箭头：往外撑开
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X - 4, c.Y + 4), false, false);
                ctx.LineTo(new Point(c.X + 4, c.Y - 4), true, false);
                ctx.LineTo(new Point(c.X - 4, c.Y - 4), true, false);
                ctx.LineTo(new Point(c.X - 4, c.Y + 4), true, false);
                ctx.LineTo(new Point(c.X - 0.5, c.Y + 0.5), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(br, 1.2), g);
        }

        private static void DrawCross(DrawingContext dc, Point c, Brush br)
        {
            var pen = new Pen(br, 1.3);
            dc.DrawLine(pen, new Point(c.X - 4, c.Y - 4), new Point(c.X + 4, c.Y + 4));
            dc.DrawLine(pen, new Point(c.X + 4, c.Y - 4), new Point(c.X - 4, c.Y + 4));
        }

        private FormattedText Text(string s, double size, Brush br, bool bold = false, double maxW = 0)
        {
            var ft = new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                bold ? Theme.Bold : Theme.Regular, size, br, _dpi);
            if (maxW > 0)
            {
                ft.MaxTextWidth = Math.Max(8, maxW);
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            return ft;
        }
    }
}
