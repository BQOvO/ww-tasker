using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WuwaChecklist
{
    internal sealed class Hit
    {
        public Rect R;
        public string Kind;
        public object Arg;
    }

    /// <summary>
    /// 主面板。全部手绘：底和字分层，底的 alpha 跟着滑杆走，字永远实色。
    /// 没有 XAML，所以每个像素都是这里算出来的。
    /// </summary>
    internal sealed class PanelView : FrameworkElement
    {
        private readonly MainWindow _host;
        private readonly List<Hit> _hits = new List<Hit>();

        private double _dpi = 1;
        private double _questScroll;
        private Rect _hover = Rect.Empty;

        public PanelView(MainWindow host)
        {
            _host = host;
            ClipToBounds = true;
            Focusable = false;
        }

        public double QuestContentHeight { get; private set; }
        public double ViewportHeight { get; private set; }
        public double RegionContentHeight { get; private set; }
        public double RegionViewportHeight { get; private set; }
        private double _regionScroll;

        // ---------------- 布局常量 ----------------
        private const double ListTop = Theme.HeaderH + Theme.TitleH;   // 78
        private const double RowBaseH = 44;
        private const double RowNoteExtra = 16;

        // ---------------- 绘制 ----------------

        protected override void OnRender(DrawingContext dc)
        {
            _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _hits.Clear();

            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 60) return;

            var st = _host.Store.Settings;
            double op = st.Opacity;

            // 1. 面板底
            var panel = new Rect(0.5, 0.5, w - 1, h - 1);
            dc.DrawGeometry(Theme.Scaled(Theme.Bg, op), new Pen(Theme.Scaled(Theme.Border, Math.Min(1.0, op + 0.25)), 1), RRect(panel, 8, 8, 8, 8));

            // 2. 头部
            var head = new Rect(0.5, 0.5, w - 1, Theme.HeaderH);
            dc.DrawGeometry(Theme.Scaled(Theme.Head, op), null, RRect(head, 8, 8, 0, 0));
            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(0.5, Theme.HeaderH + 0.5), new Point(w - 0.5, Theme.HeaderH + 0.5));

            DrawHeader(dc, w, op);

            double footerTop = h - Theme.FooterH;

            // 3. 左列
            double listBottom = footerTop;
            DrawLeft(dc, op, listBottom);

            // 4. 右列
            DrawRight(dc, w, op, footerTop);

            // 5. 底部快捷键
            DrawFooter(dc, w, h, op);

            // 6. 锁定 / 全屏提示
            if (_host.Locked) DrawLockBadge(dc, w, op);
            if (_host.FullscreenHint && !_host.Locked) DrawFullscreenHint(dc, w, h, op);

            // 7. 轻提示压在最上面
            if (!string.IsNullOrEmpty(_host.NoticeText)) DrawNotice(dc, w, h, op);
        }

        private void DrawNotice(DrawingContext dc, double w, double h, double op)
        {
            var t = Text(_host.NoticeText, 10.5, Theme.BrTxt, false, w - 24);
            var r = new Rect(12, h - Theme.FooterH - 30, w - 24, 24);
            dc.DrawGeometry(Theme.Scaled(Theme.Head, Math.Min(1, op + 0.2)),
                new Pen(Theme.BrAccDim, 1), RRect(r, 3, 3, 3, 3));
            dc.DrawText(t, new Point(r.X + 10, r.Y + (r.Height - t.Height) / 2));
        }

        private void DrawHeader(DrawingContext dc, double w, double op)
        {
            var title = Text("鸣潮 · 查漏补缺", 13.5, Theme.BrTxt, true);
            dc.DrawText(title, new Point(14, (Theme.HeaderH - title.Height) / 2));

            // 存档按钮
            double btnH = 22;
            double btnY = (Theme.HeaderH - btnH) / 2;
            string pname = _host.Store.ActiveName;
            var pt = Text(pname, 10.5, Theme.BrRTxt);
            double btnW = Math.Max(46, pt.Width + 22);
            double btnX = w - 14 - 24 - 6 - btnW;
            var btnRect = new Rect(btnX, btnY, btnW, btnH);
            dc.DrawGeometry(Theme.Scaled(Theme.TagBg, op), new Pen(Theme.Scaled(Theme.Border, op), 1), RRect(btnRect, 3, 3, 3, 3));
            pt.MaxTextWidth = btnW - 16;
            pt.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(pt, new Point(btnX + 8, btnY + (btnH - pt.Height) / 2));
            DrawChevron(dc, new Point(btnX + btnW - 9, btnY + btnH / 2), Theme.BrDim);
            _hits.Add(new Hit { R = btnRect, Kind = "profile" });
            if (btnRect.Contains(_hover)) dc.DrawGeometry(null, new Pen(Theme.BrAccDim, 1), RRect(btnRect, 3, 3, 3, 3));

            // ⋯ 按钮
            var moreRect = new Rect(w - 14 - 24, btnY, 24, btnH);
            dc.DrawGeometry(Theme.Scaled(Theme.TagBg, op), new Pen(Theme.Scaled(Theme.Border, op), 1), RRect(moreRect, 3, 3, 3, 3));
            DrawDots(dc, moreRect, Theme.BrRTxt);
            _hits.Add(new Hit { R = moreRect, Kind = "more" });
            if (moreRect.Contains(_hover)) dc.DrawGeometry(null, new Pen(Theme.BrAccDim, 1), RRect(moreRect, 3, 3, 3, 3));
        }

        private void DrawLeft(DrawingContext dc, double op, double bottom)
        {
            double w = Theme.LeftW;

            // 竖分割线
            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(w + 0.5, Theme.HeaderH), new Point(w + 0.5, bottom));

            var label = Text("区域", 10, Theme.BrSub);
            dc.DrawText(label, new Point(16, Theme.HeaderH + 6));

            double rowsTop = Theme.HeaderH + 22;
            double totalBlockH = 54;
            double rowsBottom = bottom - totalBlockH;

            // 窗口拉太矮的时候区域列表会压到「全部」那块上，裁掉多出来的
            double viewH = Math.Max(0, rowsBottom - rowsTop);
            RegionViewportHeight = viewH;
            RegionContentHeight = _host.Db.Regions.Count * Theme.RegionRowH;
            double maxScroll = Math.Max(0, RegionContentHeight - viewH);
            if (_regionScroll > maxScroll) _regionScroll = maxScroll;
            if (_regionScroll < 0) _regionScroll = 0;

            dc.PushClip(new RectangleGeometry(new Rect(0, rowsTop, w, viewH)));

            var region = _host.CurrentRegion;
            int i = 0;
            foreach (var r in _host.Db.Regions)
            {
                double y0 = rowsTop + i * Theme.RegionRowH - _regionScroll;
                i++;
                var rect = new Rect(0, y0, w, Theme.RegionRowH);
                bool sel = region != null && r.Id == region.Id;

                if (sel)
                {
                    dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op), null, new Rect(0, y0, w, Theme.RegionRowH));
                    dc.DrawRectangle(Theme.BrAcc, null, new Rect(0, y0, 2, Theme.RegionRowH));
                }
                else if (rect.Contains(_hover))
                {
                    dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op * 0.5), null, new Rect(0, y0, w, Theme.RegionRowH));
                }

                int done, total;
                _host.CountRegion(r, out done, out total);

                var nm = Text(r.Name, 12.5, sel ? Theme.BrTxt : Theme.BrRTxt, sel, w - 16 - 52);
                dc.DrawText(nm, new Point(16, y0 + 8));

                var cnt = Text(done + "/" + total, 10.5, sel ? Theme.BrAcc : Theme.BrSub);
                dc.DrawText(cnt, new Point(w - 12 - cnt.Width, y0 + 10));

                // 2px 进度条
                double bx = 16, by = y0 + 31, bw = w - 16 - 14;
                dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op), null, new Rect(bx, by, bw, 2));
                double frac = total <= 0 ? 0 : (double)done / total;
                if (frac > 0) dc.DrawRectangle(Theme.BrAcc, null, new Rect(bx, by, Math.Max(1, bw * frac), 2));

                dc.DrawLine(new Pen(Theme.Scaled(Theme.RowLine, op), 1),
                    new Point(0, y0 + Theme.RegionRowH - 0.5), new Point(w - 1, y0 + Theme.RegionRowH - 0.5));

                // 只把看得见的那部分登记成可点区域，别让裁掉的空白处还能点
                var visible = Rect.Intersect(rect, new Rect(0, rowsTop, w, viewH));
                if (!visible.IsEmpty && visible.Height > 2)
                    _hits.Add(new Hit { R = visible, Kind = "region", Arg = r.Id });
            }

            dc.Pop();

            // 区域列表能滚的时候，右边挂一小条提示
            if (RegionContentHeight > RegionViewportHeight + 1 && RegionViewportHeight > 4)
            {
                double frac = RegionViewportHeight / RegionContentHeight;
                double barH = Math.Max(12, RegionViewportHeight * frac);
                double maxY = RegionViewportHeight - barH;
                double scrollRange = RegionContentHeight - RegionViewportHeight;
                double barY = rowsTop + (scrollRange <= 0 ? 0 : maxY * (_regionScroll / scrollRange));
                dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op), null, new Rect(w - 4, barY, 2.5, barH));
            }

            // 全部进度块
            double tbY = bottom - totalBlockH;
            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(0, tbY + 0.5), new Point(w, tbY + 0.5));

            int dAll = 0, tAll = 0;
            foreach (var r in _host.Db.Regions) { int dd, tt; _host.CountRegion(r, out dd, out tt); dAll += dd; tAll += tt; }

            var a1 = Text("全部", 10.5, Theme.BrSub);
            dc.DrawText(a1, new Point(16, tbY + 10));
            var a2 = Text(dAll + "/" + tAll, 10.5, Theme.BrAcc);
            dc.DrawText(a2, new Point(w - 14 - a2.Width, tbY + 10));

            double tx = 16, ty = tbY + 28, tw = w - 16 - 14;
            dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op), null, new Rect(tx, ty, tw, 3));
            double tf = tAll <= 0 ? 0 : (double)dAll / tAll;
            if (tf > 0) dc.DrawRectangle(Theme.BrAcc, null, new Rect(tx, ty, Math.Max(1, tw * tf), 3));

            var a3 = Text(Math.Round(tf * 100) + "%", 10, Theme.BrDim);
            dc.DrawText(a3, new Point(16, ty + 7));
        }

        private void DrawRight(DrawingContext dc, double w, double op, double footerTop)
        {
            double left = Theme.LeftW + 1;
            double cw = w - left - 1;
            var region = _host.CurrentRegion;

            if (region == null)
            {
                var e = Text("没有区域", 12, Theme.BrDim);
                dc.DrawText(e, new Point(left + 14, ListTop + 14));
                return;
            }

            int done, total;
            _host.CountRegion(region, out done, out total);

            // 区域标题
            var rt = Text(region.Name, 15, Theme.BrTxt, true);
            dc.DrawText(rt, new Point(left + 14, Theme.HeaderH + 10));
            var rc = Text(done + " / " + total, 11, Theme.BrAcc);
            dc.DrawText(rc, new Point(left + 14 + rt.Width + 10, Theme.HeaderH + 14));

            // 只看未完成
            var st = _host.Store.Settings;
            var cbRect = new Rect(w - 14 - 86, Theme.HeaderH + 12, 12, 12);
            dc.DrawGeometry(null, new Pen(st.FilterIncomplete ? Theme.BrAcc : Theme.Scaled(Theme.Border, op), 1), RRect(cbRect, 2, 2, 2, 2));
            if (st.FilterIncomplete)
            {
                dc.DrawGeometry(null, new Pen(Theme.BrAcc, 1.4), CheckPath(cbRect));
            }
            var cbl = Text("只看未完成", 10.5, st.FilterIncomplete ? Theme.BrTxt : Theme.BrSub);
            dc.DrawText(cbl, new Point(cbRect.Right + 5, cbRect.Top - 1));
            _hits.Add(new Hit { R = new Rect(cbRect.X - 4, cbRect.Y - 4, 90, 20), Kind = "filter" });

            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(left, Theme.HeaderH + Theme.TitleH - 4.5),
                new Point(w - 1, Theme.HeaderH + Theme.TitleH - 4.5));

            // 任务列表
            double listH = footerTop - ListTop;
            ViewportHeight = listH;

            var shown = new List<Quest>();
            foreach (var q in region.Quests)
            {
                if (st.FilterIncomplete && _host.StateOf(q) == QuestState.Done) continue;
                shown.Add(q);
            }

            double content = 0;
            foreach (var q in shown)
            {
                content += RowBaseH;
                if (HasNoteLine(q)) content += RowNoteExtra;
            }
            QuestContentHeight = content;

            double maxScroll = Math.Max(0, content - listH);
            if (_questScroll > maxScroll) _questScroll = maxScroll;
            if (_questScroll < 0) _questScroll = 0;

            dc.PushClip(new RectangleGeometry(new Rect(left, ListTop, cw, listH)));
            double y = ListTop - _questScroll;

            if (shown.Count == 0)
            {
                var none = Text(st.FilterIncomplete ? "这个区域都做完了" : "这个区域还没有任务", 11.5, Theme.BrDim);
                dc.DrawText(none, new Point(left + 16, ListTop + 12));
            }

            foreach (var q in shown)
            {
                double rh = RowBaseH + (HasNoteLine(q) ? RowNoteExtra : 0);
                if (y + rh >= ListTop - 2 && y <= footerTop + 2)
                    DrawQuestRow(dc, q, left, y, cw, rh, op);
                y += rh;
            }
            dc.Pop();

            // 细滚动条
            if (content > listH)
            {
                double trackH = listH - 4;
                double thumbH = Math.Max(20, trackH * (listH / content));
                double thumbY = ListTop + 2 + (trackH - thumbH) * (_questScroll / maxScroll);
                dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op * 0.8), null, new Rect(w - 5, thumbY, 2.5, thumbH));
            }
        }

        private bool HasNoteLine(Quest q)
        {
            return !string.IsNullOrEmpty(q.Note) || q.Hidden;
        }

        private void DrawQuestRow(DrawingContext dc, Quest q, double left, double top, double cw, double rh, double op)
        {
            var st = _host.Store.Settings;
            int s = _host.StateOf(q);
            bool done = s == QuestState.Done;

            var rect = new Rect(left, top, cw, rh);
            if (rect.Contains(_hover) && !_host.Locked)
                dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op * 0.55), null, rect);
            if (_host.SelectedQuest == q)
                dc.DrawRectangle(Theme.BrAccDim, null, new Rect(left, top, 1.5, rh));

            // 三态圆圈
            double cx = left + 18, cy = top + 14;
            if (done)
            {
                dc.DrawEllipse(Theme.BrAcc, null, new Point(cx, cy), 7, 7);
                dc.DrawGeometry(null, new Pen(Theme.BrCheckInk, 1.6), CheckPath(new Rect(cx - 6, cy - 6, 12, 12)));
            }
            else if (s == QuestState.Doing)
            {
                dc.DrawEllipse(null, new Pen(Theme.BrAcc, 1.3), new Point(cx, cy), 6.6, 6.6);
                dc.DrawEllipse(Theme.BrAcc, null, new Point(cx, cy), 2.6, 2.6);
            }
            else
            {
                dc.DrawEllipse(null, new Pen(Theme.BrRing, 1.2), new Point(cx, cy), 6.6, 6.6);
            }
            _hits.Add(new Hit { R = new Rect(cx - 10, cy - 10, 20, 20), Kind = "circle", Arg = q });

            // 任务名
            double tx = left + 32;
            double tw = cw - 32 - 14;
            var nm = Text(q.Name, 12.5, done ? Theme.BrDone : Theme.BrTxt, false, tw);
            dc.DrawText(nm, new Point(tx, top + 5));
            if (done)
            {
                double ly = top + 5 + nm.Height * 0.58;
                dc.DrawLine(new Pen(Theme.BrStrike, 1),
                    new Point(tx, ly), new Point(tx + Math.Min(nm.WidthIncludingTrailingWhitespace, tw), ly));
            }

            // 类型标签 + 子地图
            double mx = tx;
            double metaY = top + 24;
            var tag = Text(q.TypeLabel, 9, Theme.BrTagTx);
            var tagRect = new Rect(mx, metaY, tag.Width + 8, 13);
            dc.DrawGeometry(Theme.Scaled(Theme.TagBg, op), null, RRect(tagRect, 2, 2, 2, 2));
            dc.DrawText(tag, new Point(mx + 4, metaY + 1.5));
            mx = tagRect.Right + 6;

            if (!string.IsNullOrEmpty(q.SubArea))
            {
                var sub = Text(q.SubArea, 10, Theme.BrSub, false, tw - (mx - tx) - 4);
                dc.DrawText(sub, new Point(mx, metaY + 1));
            }

            // 备注行
            if (HasNoteLine(q))
            {
                double ny = top + 40;
                double nx = tx;
                if (q.Hidden)
                {
                    // 隐藏任务先打个小三角，后面再跟备注（合并后两条信息都可能在）
                    var tri = new Point[3];
                    tri[0] = new Point(tx, ny + 4);
                    tri[1] = new Point(tx + 5, ny + 4);
                    tri[2] = new Point(tx + 2.5, ny + 8);
                    dc.DrawGeometry(Theme.BrHide, null, Poly(tri));
                    nx = tx + 9;
                }

                if (!string.IsNullOrEmpty(q.Note))
                {
                    var nt = Text(q.Note, 10, Theme.BrNote, false, tw - (nx - tx));
                    dc.DrawText(nt, new Point(nx, ny));
                }
                else if (q.Hidden)
                {
                    var ht = Text("隐藏任务 · 无追踪", 10, Theme.BrHide, false, tw - 10);
                    dc.DrawText(ht, new Point(nx, ny));
                }
            }

            dc.DrawLine(new Pen(Theme.Scaled(Theme.RowLine, op), 1),
                new Point(left + 8, top + rh - 0.5), new Point(left + cw - 8, top + rh - 0.5));

            _hits.Add(new Hit { R = new Rect(left + 26, top, cw - 26, rh), Kind = "quest", Arg = q });
        }

        private void DrawFooter(DrawingContext dc, double w, double h, double op)
        {
            double top = h - Theme.FooterH;
            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(0.5, top + 0.5), new Point(w - 0.5, top + 0.5));

            var st = _host.Store.Settings;
            string hint = HkDigits(st);
            var ft = Text(hint, 9.5, Theme.BrFoot);
            dc.DrawText(ft, new Point(14, top + (Theme.FooterH - ft.Height) / 2));
        }

        private static string HkDigits(AppSettings st)
        {
            // 把三个快捷键的「后缀键」抽出来排一行，像静态稿那样
            string a = LastKey(st.HkToggle), b = LastKey(st.HkLock), c = LastKey(st.HkFold);
            return "Ctrl+Alt+  " + a + " 显隐    " + b + " 穿透    " + c + " 折叠";
        }

        private static string LastKey(string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return "?";
            var parts = gesture.Split('+');
            return parts[parts.Length - 1].Trim();
        }

        private void DrawLockBadge(DrawingContext dc, double w, double op)
        {
            var t = Text("已锁定 · 点击穿透", 9.5, Theme.BrAcc);
            var r = new Rect(14, Theme.HeaderH + 3, t.Width + 12, 15);
            dc.DrawGeometry(Theme.Scaled(Theme.TagBg, op), null, RRect(r, 2, 2, 2, 2));
            dc.DrawText(t, new Point(r.X + 6, r.Y + 1));
        }

        private void DrawFullscreenHint(DrawingContext dc, double w, double h, double op)
        {
            var txt = Text("独占全屏时看不见我，Ctrl+Alt+W 试一下", 10, Theme.BrNote, false, w - 40);
            var r = new Rect(12, h - Theme.FooterH - 26, w - 24, 22);
            dc.DrawGeometry(Theme.Scaled(Theme.Head, Math.Min(1, op + 0.15)), new Pen(Theme.BrNote, 1), RRect(r, 3, 3, 3, 3));
            dc.DrawText(txt, new Point(r.X + 8, r.Y + (r.Height - txt.Height) / 2));
        }

        // ---------------- 输入 ----------------

        private Hit HitAt(Point p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].R.Contains(p)) return _hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            var hit = HitAt(p);
            var r = hit == null ? Rect.Empty : hit.R;
            if (r != _hover)
            {
                _hover = r;
                InvalidateVisual();
            }
            Cursor = hit != null && !_host.Locked ? Cursors.Hand : Cursors.Arrow;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != Rect.Empty) { _hover = Rect.Empty; InvalidateVisual(); }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            var p = e.GetPosition(this);
            if (p.X < Theme.LeftW)
            {
                // 区域列表：窗口矮的时候可以滚
                double rmax = Math.Max(0, RegionContentHeight - RegionViewportHeight);
                if (rmax <= 0) return;
                _regionScroll -= e.Delta * 0.5;
                if (_regionScroll < 0) _regionScroll = 0;
                if (_regionScroll > rmax) _regionScroll = rmax;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            _questScroll -= e.Delta * 0.6;
            if (_questScroll < 0) _questScroll = 0;
            double max = Math.Max(0, QuestContentHeight - ViewportHeight);
            if (_questScroll > max) _questScroll = max;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_host.Locked) return;

            var p = e.GetPosition(this);
            var hit = HitAt(p);

            if (hit != null)
            {
                switch (hit.Kind)
                {
                    case "region":
                        _host.SelectRegion((string)hit.Arg);
                        _questScroll = 0;
                        InvalidateVisual();
                        break;
                    case "circle":
                        var q1 = (Quest)hit.Arg;
                        _host.CycleState(q1);
                        InvalidateVisual();
                        break;
                    case "quest":
                        var q2 = (Quest)hit.Arg;
                        if (e.ClickCount >= 2) _host.BeginNoteEdit(q2, Rect.Empty);
                        else { _host.SelectedQuest = q2; InvalidateVisual(); }
                        break;
                    case "filter":
                        _host.Store.Settings.FilterIncomplete = !_host.Store.Settings.FilterIncomplete;
                        _host.SaveSoon();
                        InvalidateVisual();
                        break;
                    case "profile":
                        DeferMenu(true);
                        break;
                    case "more":
                        DeferMenu(false);
                        break;                }
                e.Handled = true;
                return;
            }

            if (p.Y < Theme.HeaderH)
            {
                _host.BeginDrag();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 菜单不能在 MouseDown 里开。Popup 的 StaysOpen=false 会立刻抓鼠标，
        /// 同一个点击的 MouseUp 就被它当成「点了外面」，菜单一闪就没。
        /// 所以推到鼠标抬起并且这一轮消息走完之后再开。
        /// </summary>
        private void DeferMenu(bool profile)
        {
            MouseButtonEventHandler up = null;
            up = (s, e) =>
            {
                MouseLeftButtonUp -= up;
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (profile) _host.ShowProfileMenu();
                    else _host.ShowMainMenu();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            MouseLeftButtonUp += up;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (_host.Locked) return;
            var p = e.GetPosition(this);
            var hit = HitAt(p);
            if (hit != null && hit.Kind == "quest")
            {
                _host.ShowQuestMenu((Quest)hit.Arg, this, p);
                e.Handled = true;
            }
        }

        /// <summary>拿到某条任务备注行的屏幕位置，给备注编辑框定位。</summary>
        public Rect GetQuestNoteRect(Quest q)
        {
            var st = _host.Store.Settings;
            var region = _host.CurrentRegion;
            if (region == null) return new Rect(40, 100, 200, 18);

            double y = ListTop - _questScroll;
            foreach (var item in region.Quests)
            {
                if (st.FilterIncomplete && _host.StateOf(item) == QuestState.Done) continue;
                double rh = RowBaseH + (HasNoteLine(item) ? RowNoteExtra : 0);
                if (item == q)
                {
                    double noteY = y + (HasNoteLine(q) ? 40 : 40);
                    return new Rect(Theme.LeftW + 32, noteY, ActualWidth - Theme.LeftW - 46, 16);
                }
                y += rh;
            }
            return new Rect(Theme.LeftW + 32, 100, ActualWidth - Theme.LeftW - 46, 16);
        }

        public void ScrollQuestTo(Quest q) { }

        // ---------------- 画小件 ----------------

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

        /// <summary>圆角矩形，四个角半径可分别给。</summary>
        public static Geometry RRect(Rect r, double tl, double tr, double br, double bl)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(r.Left + tl, r.Top), true, true);
                ctx.LineTo(new Point(r.Right - tr, r.Top), true, false);
                if (tr > 0) ctx.ArcTo(new Point(r.Right, r.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(r.Right, r.Bottom - br), true, false);
                if (br > 0) ctx.ArcTo(new Point(r.Right - br, r.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(r.Left + bl, r.Bottom), true, false);
                if (bl > 0) ctx.ArcTo(new Point(r.Left, r.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(r.Left, r.Top + tl), true, false);
                if (tl > 0) ctx.ArcTo(new Point(r.Left + tl, r.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        public static Geometry CheckPath(Rect r)
        {
            double x = r.Left, y = r.Top, w = r.Width, h = r.Height;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x + w * 0.20, y + h * 0.52), false, false);
                ctx.LineTo(new Point(x + w * 0.42, y + h * 0.74), true, false);
                ctx.LineTo(new Point(x + w * 0.80, y + h * 0.28), true, false);
            }
            g.Freeze();
            return g;
        }

        public static Geometry Poly(Point[] pts)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
            }
            g.Freeze();
            return g;
        }

        private static void DrawChevron(DrawingContext dc, Point c, Brush br)
        {
            var pts = new[] { new Point(c.X - 3, c.Y - 1.5), new Point(c.X, c.Y + 1.5), new Point(c.X + 3, c.Y - 1.5) };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                ctx.LineTo(pts[1], true, false);
                ctx.LineTo(pts[2], true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(br, 1.2), g);
        }

        private static void DrawDots(DrawingContext dc, Rect r, Brush br)
        {
            double cy = r.Top + r.Height / 2;
            double cx = r.Left + r.Width / 2;
            for (int i = -1; i <= 1; i++)
                dc.DrawEllipse(br, null, new Point(cx + i * 4, cy), 1.2, 1.2);
        }
    }
}
