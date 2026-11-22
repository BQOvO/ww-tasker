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

        /// <summary>这份滚动位置属于哪个（区域/类型/搜索词）组合。切走要存，切回来要还原。</summary>
        private string _scrollKey;

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

        /// <summary>
        /// 当前这一屏对应哪个键：区域 + 类型 + 搜索词。切区域、切签、改搜索词都会换键，
        /// 换键之前把现在的滚动位置存进设置，新键没有记录就从头开始。
        /// </summary>
        private string CurrentScrollKey()
        {
            var st = _host.Store.Settings;
            string q = (st.SearchText ?? "").Trim();
            if (q.Length > 0) return "q:" + q.ToLowerInvariant();
            var r = _host.CurrentRegion;
            return "r:" + (r == null ? "" : r.Id) + "|" + st.TypeFilter;
        }

        /// <summary>把当前滚动位置写回设置（切走、关窗、滚动停下时调）。</summary>
        public void SaveScroll()
        {
            var st = _host.Store.Settings;
            string k = _scrollKey;
            if (string.IsNullOrEmpty(k)) return;
            if (_questScroll > 0.5) st.Scrolls[k] = _questScroll;
            else st.Scrolls.Remove(k);
            st.RegionScroll = _regionScroll > 0.5 ? _regionScroll : 0;
        }

        /// <summary>切到新的一屏：先存旧的，再按新键还原。</summary>
        public void SwitchScroll()
        {
            SaveScroll();
            string k = CurrentScrollKey();
            _scrollKey = k;
            double v;
            _questScroll = _host.Store.Settings.Scrolls.TryGetValue(k, out v) ? v : 0;
        }

        // ---------------- 布局常量 ----------------
        private const double TabsH = 30;
        private const double RowBaseH = 44;
        private const double RowNoteExtra = 16;
        private const double ChapterH = 26;

        /// <summary>右列任务列表的起点。只有一种类型时不留分页签的位置。</summary>
        private double _listTop = Theme.HeaderH + Theme.TitleH + TabsH;

        /// <summary>左列的一行：章节头，或者章节里的一个地区。</summary>
        private sealed class LeftRow
        {
            public string Kind;        // "chapter" / "region"
            public string Chapter;
            public Region Region;
            public double H;
        }

        // ---------------- 绘制 ----------------

        protected override void OnRender(DrawingContext dc)
        {
            _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _hits.Clear();

            // 头一次画：把上次关窗时的滚动位置接回来
            if (_scrollKey == null)
            {
                SwitchScroll();
                _regionScroll = _host.Store.Settings.RegionScroll;
            }

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
            var st = _host.Store.Settings;
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

            // 搜索药丸：夹在标题和存档按钮中间，宽度跟着窗口走
            DrawSearchPill(dc, op, title.Width + 14 + 12, btnX - 8);

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

        /// <summary>
        /// 头顶那枚搜索药丸。空着的时候只画个放大镜和「搜索」，有词就显示词和右边的 ×。
        /// 位置是标题右边到存档按钮左边之间，窗口窄到放不下就不画（免得挤成一条缝）。
        /// </summary>
        private void DrawSearchPill(DrawingContext dc, double op, double x0, double x1)
        {
            double w = x1 - x0;
            if (w < 26) return;             // 真挤不下了，连图标都摊不开

            var st = _host.Store.Settings;
            string q = st.SearchText ?? "";
            bool on = q.Trim().Length > 0;
            // 窄的时候只留放大镜和 ×，不写字——字挤成一条看不清，还占着地方
            bool compact = w < 64;

            double btnH = 22;
            double y = (Theme.HeaderH - btnH) / 2;
            var rect = new Rect(x0, y, w, btnH);

            dc.DrawGeometry(Theme.Scaled(Theme.TagBg, on ? Math.Min(1, op + 0.1) : op),
                new Pen(on ? Theme.BrAccDim : Theme.Scaled(Theme.Border, op), 1),
                RRect(rect, 11, 11, 11, 11));
            if (rect.Contains(_hover)) dc.DrawGeometry(null, new Pen(Theme.BrAccDim, 1), RRect(rect, 11, 11, 11, 11));

            // 放大镜：一个圈加一根柄，别指望字体里那个。
            // 挤到只剩二十几像素的时候它跟 × 会压在一块儿，这时候让位给 ×
            if (!compact || !on)
            {
                var mc = new Point(compact ? x0 + w / 2 : x0 + 12, y + btnH / 2);
                dc.DrawEllipse(null, new Pen(on ? Theme.BrAcc : Theme.BrDim, 1.2), mc, 4, 4);
                dc.DrawLine(new Pen(on ? Theme.BrAcc : Theme.BrDim, 1.2),
                    new Point(mc.X + 3, mc.Y + 3), new Point(mc.X + 6, mc.Y + 6));
            }

            double clearW = on ? 16 : 0;
            double tx = x0 + 21;
            double tw = w - 21 - 8 - clearW;
            if (!compact && tw > 12)
            {
                var txt = on
                    ? Text(q, 10.5, Theme.BrTxt, false, tw)
                    : Text("搜索任务名", 10.5, Theme.BrSub, false, tw);
                dc.DrawText(txt, new Point(tx, y + (btnH - txt.Height) / 2));
            }

            // 整块先登记，× 后登记——HitAt 是倒着找的，后登记的先命中
            _hits.Add(new Hit { R = new Rect(rect.X, rect.Y, rect.Width - clearW, rect.Height), Kind = "search" });

            if (on)
            {
                var cr = new Rect(rect.Right - 18, y, 16, btnH);
                var cc = new Point(cr.X + 8, cr.Y + btnH / 2);
                var pen = new Pen(Theme.BrSub, 1.2);
                dc.DrawLine(pen, new Point(cc.X - 3, cc.Y - 3), new Point(cc.X + 3, cc.Y + 3));
                dc.DrawLine(pen, new Point(cc.X - 3, cc.Y + 3), new Point(cc.X + 3, cc.Y - 3));
                _hits.Add(new Hit { R = cr, Kind = "searchclear" });
            }
        }

        private void DrawLeft(DrawingContext dc, double op, double bottom)
        {
            double w = Theme.LeftW;

            // 竖分割线
            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(w + 0.5, Theme.HeaderH), new Point(w + 0.5, bottom));

            // 按版本分的时候这一列就是版本号，标题跟着换，不然写着「区域」列着「2.4」很怪
            bool byVersion = _host.Db.Regions.Count > 0
                && string.IsNullOrEmpty(_host.Db.Regions[0].Chapter);
            var label = Text(byVersion ? "版本" : "区域", 10, Theme.BrSub);
            dc.DrawText(label, new Point(16, Theme.HeaderH + 6));

            double rowsTop = Theme.HeaderH + 22;
            double totalBlockH = 54;
            double rowsBottom = bottom - totalBlockH;

            // 窗口拉太矮的时候区域列表会压到「全部」那块上，裁掉多出来的
            double viewH = Math.Max(0, rowsBottom - rowsTop);
            RegionViewportHeight = viewH;

            var rows = BuildLeftRows();
            double contentH = 0;
            foreach (var x in rows) contentH += x.H;
            RegionContentHeight = contentH;

            double maxScroll = Math.Max(0, RegionContentHeight - viewH);
            if (_regionScroll > maxScroll) _regionScroll = maxScroll;
            if (_regionScroll < 0) _regionScroll = 0;

            dc.PushClip(new RectangleGeometry(new Rect(0, rowsTop, w, viewH)));

            var region = _host.CurrentRegion;
            double y0 = rowsTop - _regionScroll;
            foreach (var row in rows)
            {
                if (row.Kind == "chapter") DrawChapterRow(dc, op, w, row, y0, rowsTop, viewH);
                else DrawRegionRow(dc, op, w, row, y0, region, rowsTop, viewH);
                y0 += row.H;
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

        /// <summary>
        /// 左列的行：章节头 + 该章节的地区。展开的那一章才列地区，其余只留一行标题。
        /// 一次只开一章——这是他自己选的「按版本折叠，只展开当前章节」。
        ///
        /// 0.7.0 起任务库改成按版本分组（1.0 / 1.1 / … / 3.8），数据里不再有 chapter，
        /// 这时整列就是一排版本平铺，滚着看，不折了——23 个版本折成 3 组反而多按一下。
        /// </summary>
        private List<LeftRow> BuildLeftRows()
        {
            var rows = new List<LeftRow>();
            string open = _host.OpenChapter;
            bool hideDone = _host.Store.Settings.HideDoneRegions;
            var cur0 = _host.CurrentRegion;

            // 按章节首次出现的顺序分组（Regions 已经按 order 排过）
            var order = new List<string>();
            var map = new Dictionary<string, List<Region>>(StringComparer.Ordinal);
            foreach (var r in _host.Db.Regions)
            {
                // 做完的藏起来，但当前正选着的那条永远留着——不然右列写着「2.0」，
                // 左列找不到它在哪儿，看着像程序坏了
                if (hideDone && (cur0 == null || r.Id != cur0.Id))
                {
                    int dd, tt;
                    _host.CountRegion(r, out dd, out tt);
                    if (tt > 0 && dd >= tt) continue;
                }

                string ch = string.IsNullOrEmpty(r.Chapter) ? "其他" : r.Chapter;
                if (!map.ContainsKey(ch))
                {
                    map[ch] = new List<Region>();
                    order.Add(ch);
                }
                map[ch].Add(r);
            }

            foreach (var ch in order)
            {
                // 整份库都没有章节（按版本分的那种），平铺
                bool noChapters = string.IsNullOrEmpty(_host.Db.Regions[0].Chapter);
                if (noChapters)
                {
                    foreach (var r in map[ch])
                        rows.Add(new LeftRow { Kind = "region", Chapter = null, Region = r, H = Theme.RegionRowH });
                    continue;
                }

                // 这一章只有一张地图的时候，标题和地图名会重成「黑海岸 / 黑海岸」，
                // 折一层也是白折，直接把地图摊到顶层。
                if (map[ch].Count == 1)
                {
                    rows.Add(new LeftRow { Kind = "region", Chapter = null, Region = map[ch][0], H = Theme.RegionRowH });
                    continue;
                }

                bool openThis = string.Equals(ch, open, StringComparison.Ordinal);
                rows.Add(new LeftRow { Kind = "chapter", Chapter = ch, H = ChapterH });

                if (!openThis)
                {
                    // 收起的章节里要是装着当前选中的地图，那一行还是留着——
                    // 不然右列写着「今州」，左列找不到它在哪一章。
                    var cur = _host.CurrentRegion;
                    if (cur != null)
                    {
                        foreach (var r in map[ch])
                            if (r.Id == cur.Id)
                            {
                                rows.Add(new LeftRow { Kind = "region", Chapter = ch, Region = r, H = Theme.RegionRowH });
                                break;
                            }
                    }
                    continue;
                }
                foreach (var r in map[ch])
                    rows.Add(new LeftRow { Kind = "region", Chapter = ch, Region = r, H = Theme.RegionRowH });
            }
            return rows;
        }

        private void DrawChapterRow(DrawingContext dc, double op, double w, LeftRow row, double y0, double rowsTop, double viewH)
        {
            bool open = string.Equals(row.Chapter, _host.OpenChapter, StringComparison.Ordinal);
            var rect = new Rect(0, y0, w, row.H);
            bool hov = !_host.Locked && rect.Contains(_hover);

            if (hov) dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op * 0.45), null, rect);

            // 展开的小三角朝下，收起朝右——全手绘，雅黑里的 ▸ 会变空心方块
            var c = new Point(13, y0 + row.H / 2);
            if (open) DrawChevronDown(dc, c, Theme.BrAcc);
            else DrawChevronRight(dc, c, Theme.BrSub);

            // 章节名会挤，用 TextFit 从 10.5 一路缩到 8.5
            var nm = TextFit(row.Chapter, new[] { 10.5, 9.5, 8.5 },
                open ? Theme.BrAcc : Theme.BrSub, open, w - 26 - 8);
            dc.DrawText(nm, new Point(24, y0 + (row.H - nm.Height) / 2));

            dc.DrawLine(new Pen(Theme.Scaled(Theme.RowLine, op * 0.7), 1),
                new Point(10, y0 + row.H - 0.5), new Point(w - 8, y0 + row.H - 0.5));

            var visible = Rect.Intersect(rect, new Rect(0, rowsTop, w, viewH));
            if (!visible.IsEmpty && visible.Height > 2)
                _hits.Add(new Hit { R = visible, Kind = "chapter", Arg = row.Chapter });
        }

        private void DrawRegionRow(DrawingContext dc, double op, double w, LeftRow row, double y0, Region current, double rowsTop, double viewH)
        {
            var r = row.Region;
            var rect = new Rect(0, y0, w, row.H);
            bool sel = current != null && r.Id == current.Id;

            if (sel)
            {
                dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op), null, new Rect(0, y0, w, row.H));
                dc.DrawRectangle(Theme.BrAcc, null, new Rect(0, y0, 2, row.H));
            }
            else if (rect.Contains(_hover))
            {
                dc.DrawRectangle(Theme.Scaled(Theme.SelBg, op * 0.5), null, new Rect(0, y0, w, row.H));
            }

            int done, total;
            _host.CountRegion(r, out done, out total);

            // 挂在章节下面才缩进一格；顶层的地图不缩
            double ox = row.Chapter == null ? 16 : 26;
            var nm = TextFit(r.Name, new[] { 12.5, 11.5, 10.5, 9.5 },
                sel ? Theme.BrTxt : Theme.BrRTxt, sel, w - ox - 42);
            dc.DrawText(nm, new Point(ox, y0 + 6 + Math.Max(0, (22 - nm.Height) / 2)));

            var cnt = Text(done + "/" + total, 10.5, sel ? Theme.BrAcc : Theme.BrSub);
            dc.DrawText(cnt, new Point(w - 12 - cnt.Width, y0 + 10));

            // 2px 进度条
            double bx = ox, by = y0 + 31, bw = w - ox - 14;
            dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op), null, new Rect(bx, by, bw, 2));
            double frac = total <= 0 ? 0 : (double)done / total;
            if (frac > 0) dc.DrawRectangle(Theme.BrAcc, null, new Rect(bx, by, Math.Max(1, bw * frac), 2));

            dc.DrawLine(new Pen(Theme.Scaled(Theme.RowLine, op), 1),
                new Point(0, y0 + row.H - 0.5), new Point(w - 1, y0 + row.H - 0.5));

            // 只把看得见的那部分登记成可点区域，别让裁掉的空白处还能点
            var visible = Rect.Intersect(rect, new Rect(0, rowsTop, w, viewH));
            if (!visible.IsEmpty && visible.Height > 2)
                _hits.Add(new Hit { R = visible, Kind = "region", Arg = r.Id });
        }

        private void DrawRight(DrawingContext dc, double w, double op, double footerTop)
        {
            double left = Theme.LeftW + 1;
            double cw = w - left - 1;
            var st = _host.Store.Settings;
            bool searching = !string.IsNullOrEmpty((st.SearchText ?? "").Trim());

            // 区域/类型/搜索词变了就是换了一屏，滚动位置各自记
            if (CurrentScrollKey() != _scrollKey) SwitchScroll();

            // 搜索时右列换成跨版本的搜索结果，没有「当前区域」这回事了
            var region = searching ? null : _host.CurrentRegion;
            var shown = BuildShown();

            if (!searching && region == null)
            {
                var e = Text("还没有任务库", 12, Theme.BrDim);
                dc.DrawText(e, new Point(left + 14, _listTop + 14));
                return;
            }

            // 标题行：区域名 + 计数，或者「搜索结果 N 条」
            double headRight;
            if (searching)
            {
                var ht = Text("搜索结果", 15, Theme.BrTxt, true);
                dc.DrawText(ht, new Point(left + 14, Theme.HeaderH + 10));
                var hc = Text(shown.Count + " 条", 11, Theme.BrAcc);
                dc.DrawText(hc, new Point(left + 14 + ht.Width + 10, Theme.HeaderH + 14));
                headRight = left + 14 + ht.Width + 10 + hc.Width;
            }
            else
            {
                int done, total;
                _host.CountRegion(region, out done, out total);
                var rt = Text(region.Name, 15, Theme.BrTxt, true);
                dc.DrawText(rt, new Point(left + 14, Theme.HeaderH + 10));
                var rc = Text(done + " / " + total, 11, Theme.BrAcc);
                dc.DrawText(rc, new Point(left + 14 + rt.Width + 10, Theme.HeaderH + 14));
                headRight = left + 14 + rt.Width + 10 + rc.Width;
            }

            // 只看未完成：窗口拉到最窄的时候这里会跟标题叠上，叠上就不画了，
            // 反正这个开关在 ⋯ 菜单里也有
            if (w - 14 - 86 - 8 > headRight)
            {
                var cbRect = new Rect(w - 14 - 86, Theme.HeaderH + 12, 12, 12);
                dc.DrawGeometry(null, new Pen(st.FilterIncomplete ? Theme.BrAcc : Theme.Scaled(Theme.Border, op), 1), RRect(cbRect, 2, 2, 2, 2));
                if (st.FilterIncomplete)
                {
                    dc.DrawGeometry(null, new Pen(Theme.BrAcc, 1.4), CheckPath(cbRect));
                }
                var cbl = Text("只看未完成", 10.5, st.FilterIncomplete ? Theme.BrTxt : Theme.BrSub);
                dc.DrawText(cbl, new Point(cbRect.Right + 5, cbRect.Top - 1));
                _hits.Add(new Hit { R = new Rect(cbRect.X - 4, cbRect.Y - 4, 90, 20), Kind = "filter" });
            }

            dc.DrawLine(new Pen(Theme.Scaled(Theme.Line, op), 1),
                new Point(left, Theme.HeaderH + Theme.TitleH - 4.5),
                new Point(w - 1, Theme.HeaderH + Theme.TitleH - 4.5));

            // 类型分页签：搜索时没有意义（结果是跨版本的），直接不给
            if (searching)
            {
                _listTop = Theme.HeaderH + Theme.TitleH;
            }
            else
            {
                DrawTabs(dc, op, left, cw, w, region, shown);
            }

            // 任务列表
            double listH = footerTop - _listTop;
            ViewportHeight = listH;

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

            dc.PushClip(new RectangleGeometry(new Rect(left, _listTop, cw, listH)));
            double y = _listTop - _questScroll;

            if (shown.Count == 0)
            {
                string empty;
                if (searching) empty = "没搜到叫这个的任务";
                else if (st.FilterIncomplete) empty = "这个筛法下都做完了";
                else empty = "这一栏还没有任务";
                var none = Text(empty, 11.5, Theme.BrDim);
                dc.DrawText(none, new Point(left + 16, _listTop + 12));
            }

            foreach (var q in shown)
            {
                double rh = RowBaseH + (HasNoteLine(q) ? RowNoteExtra : 0);
                if (y + rh >= _listTop - 2 && y <= footerTop + 2)
                    DrawQuestRow(dc, q, left, y, cw, rh, op, searching);
                y += rh;
            }
            dc.Pop();

            // 细滚动条
            if (content > listH)
            {
                double trackH = listH - 4;
                double thumbH = Math.Max(20, trackH * (listH / content));
                double thumbY = _listTop + 2 + (trackH - thumbH) * (_questScroll / maxScroll);
                dc.DrawRectangle(Theme.Scaled(Theme.BarBg, op * 0.8), null, new Rect(w - 5, thumbY, 2.5, thumbH));
            }
        }

        private bool HasNoteLine(Quest q)
        {
            return !string.IsNullOrEmpty(q.Note) || q.Hidden;
        }

        /// <summary>当前区域里实际出现过的类型，按类型表的顺序。</summary>
        private List<string> TypesIn(Region region)
        {
            var order = new[] { QuestType.Main, QuestType.Comp, QuestType.Tale,
                                QuestType.Side, QuestType.Expl, QuestType.Hidden,
                                QuestType.Event };
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in region.Quests) have.Add(q.Type);

            var list = new List<string>();
            foreach (var t in order) if (have.Contains(t)) list.Add(t);
            foreach (var t in have) if (!list.Contains(t)) list.Add(t);
            return list;
        }

        /// <summary>当前该显示哪些任务：类型分页签 + 只看未完成，两个一起过。</summary>
        private List<Quest> BuildShown()
        {
            return _host.CurrentShown();
        }

        private void DrawTabs(DrawingContext dc, double op, double left, double cw, double w, Region region, List<Quest> shown)
        {
            var types = TypesIn(region);
            if (types.Count <= 1)
            {
                _listTop = Theme.HeaderH + Theme.TitleH;
                return;
            }
            _listTop = Theme.HeaderH + Theme.TitleH + TabsH;

            string cur = _host.Store.Settings.TypeFilter;
            if (!string.IsNullOrEmpty(cur) && !types.Contains(cur)) cur = "";

            // 只有「全部」+ 实际存在的类型，别把六种全摆出来撑爆
            var labels = new List<string> { "全部" };
            var keys = new List<string> { "" };
            foreach (var t in types) { labels.Add(QuestType.Label(t)); keys.Add(t); }

            double tabH = 21;
            double ty = Theme.HeaderH + Theme.TitleH + 3;
            double x0 = left + 14;
            double right = w - 12;

            // 签一多就会顶出右边缘（黎那汐塔有 7 个）。先按最宽松的排，放不下就
            // 整排收紧字号、边距、间距，取第一个装得下的方案；全试过还装不下就硬裁。
            var layouts = new[]
            {
                new { Size = 10.5, PadX = 14.0, Gap = 4.0 },
                new { Size = 10.0, PadX = 12.0, Gap = 3.0 },
                new { Size = 9.5, PadX = 10.0, Gap = 3.0 },
                new { Size = 9.0, PadX = 8.0, Gap = 2.0 },
            };

            double[] widths = null;
            double padX = 14, gap = 4, size = 10.5;
            foreach (var l in layouts)
            {
                var ws = new double[labels.Count];
                double sum = 0;
                for (int i = 0; i < labels.Count; i++)
                {
                    bool on = string.Equals(keys[i], cur, StringComparison.Ordinal);
                    var m = Text(labels[i], l.Size, on ? Theme.BrAcc : Theme.BrSub, on);
                    ws[i] = Math.Round(m.Width + l.PadX);
                    sum += ws[i];
                }
                sum += l.Gap * (labels.Count - 1);
                widths = ws; padX = l.PadX; gap = l.Gap; size = l.Size;
                if (x0 + sum <= right) break;
            }

            dc.PushClip(new RectangleGeometry(new Rect(left, ty - 2, cw, tabH + 4)));
            double x = x0;
            for (int i = 0; i < labels.Count; i++)
            {
                bool on = string.Equals(keys[i], cur, StringComparison.Ordinal);
                var t = Text(labels[i], size, on ? Theme.BrAcc : Theme.BrSub, on);
                double tw = widths[i];

                var r = new Rect(x, ty, tw, tabH);
                if (on) dc.DrawRectangle(Theme.BrAcc, null, new Rect(x, ty + tabH - 1.5, tw, 1.5));
                dc.DrawText(t, new Point(x + (tw - t.Width) / 2, ty + (tabH - t.Height) / 2));

                _hits.Add(new Hit { R = r, Kind = "tab", Arg = keys[i] });
                x += tw + gap;
            }
            dc.Pop();
        }

        private void DrawQuestRow(DrawingContext dc, Quest q, double left, double top, double cw, double rh, double op, bool withOrigin = false)
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
            double metaY = top + 23;
            var tag = Text(q.TypeLabel, 10, Theme.TypeTagInk, true);
            var tagRect = new Rect(mx, metaY, tag.Width + 10, 15);
            dc.DrawGeometry(Theme.TypeBack(q.Type, op), null, RRect(tagRect, 2.5, 2.5, 2.5, 2.5));
            dc.DrawText(tag, new Point(mx + 5, metaY + 1.5));
            mx = tagRect.Right + 7;

            // 搜索结果里混着各个版本，得标一下这条是哪儿的
            if (withOrigin)
            {
                var reg = _host.Db.FindRegion(q.RegionId);
                string origin = reg == null ? "" : reg.Name;
                if (!string.IsNullOrEmpty(origin))
                {
                    var ot = Text(origin, 10, Theme.BrDim, false, 40);
                    dc.DrawText(ot, new Point(mx, metaY + 1));
                    mx += ot.WidthIncludingTrailingWhitespace + 6;
                }
            }

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
                    case "chapter":
                        _host.ToggleChapter((string)hit.Arg);
                        _regionScroll = 0;
                        InvalidateVisual();
                        break;
                    case "tab":
                        // 别在这里把 _questScroll 清零——换屏前要先把它存回旧的那一屏
                        _host.Store.Settings.TypeFilter = (string)hit.Arg;
                        _host.SaveSoon();
                        InvalidateVisual();
                        break;
                    case "region":
                        _host.SelectRegion((string)hit.Arg);
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
                    case "search":
                        _host.BeginSearch();
                        break;
                    case "searchclear":
                        _host.ClearSearch();
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
                        break;
                }
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

            double y = _listTop - _questScroll;
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
                // 一律单行：中文按字符折行，多折一行就会顶掉下面那根进度条或标签
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            return ft;
        }

        /// <summary>
        /// 单行、放得下就用原字号，放不下就逐级缩，缩到底还放不下才省略号。
        /// 不给 MaxLineCount=1 的话中文会按字符折行，把下面那根进度条顶掉。
        /// </summary>
        private FormattedText TextFit(string s, double[] sizes, Brush br, bool bold, double maxW)
        {
            FormattedText ft = null;
            foreach (var size in sizes)
            {
                // 先量一次「不限宽」的自然宽度——在 MaxTextWidth 里量永远是放得下的，
                // 那样第一档就被判通过，字号根本不会缩
                var probe = new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    bold ? Theme.Bold : Theme.Regular, size, br, _dpi);
                ft = probe;
                if (probe.WidthIncludingTrailingWhitespace <= maxW) break;
            }
            ft.MaxTextWidth = Math.Max(8, maxW);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
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

        /// <summary>章节展开：三角尖朝下。</summary>
        private static void DrawChevronDown(DrawingContext dc, Point c, Brush br)
        {
            var pts = new[] { new Point(c.X - 3, c.Y - 1.6), new Point(c.X + 3, c.Y - 1.6), new Point(c.X, c.Y + 2.2) };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                ctx.LineTo(pts[1], true, false);
                ctx.LineTo(pts[2], true, false);
            }
            g.Freeze();
            dc.DrawGeometry(br, null, g);
        }

        /// <summary>章节收起：三角尖朝右。</summary>
        private static void DrawChevronRight(DrawingContext dc, Point c, Brush br)
        {
            var pts = new[] { new Point(c.X - 1.6, c.Y - 3), new Point(c.X + 2.2, c.Y), new Point(c.X - 1.6, c.Y + 3) };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                ctx.LineTo(pts[1], true, false);
                ctx.LineTo(pts[2], true, false);
            }
            g.Freeze();
            dc.DrawGeometry(br, null, g);
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
