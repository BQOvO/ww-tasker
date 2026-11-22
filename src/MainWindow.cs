using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WuwaChecklist
{
    internal sealed class MainWindow : Window
    {
        public readonly Store Store;
        public QuestDatabase Db;
        public Quest SelectedQuest;

        public bool Locked { get; private set; }
        public bool FullscreenHint { get; private set; }
        public bool Folded { get; private set; }

        private readonly PanelView _panel;
        private readonly StripeView _stripe;
        private readonly Grid _root;
        private HotkeyManager _hotkeys;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _fullTimer;
        private TextBox _noteBox;
        private Point _dragOrigin;
        private Rect _dragStart;
        private bool _dragging;

        public MainWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = true;
            ResizeMode = ResizeMode.NoResize;
            Title = "鸣潮 · 查漏补缺";
            Icon = AppIcon.Load();
            Width = 392;
            Height = 440;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

            Store = WuwaChecklist.Store.Load();
            if (!string.IsNullOrEmpty(Store.LastError)) App.ReportSoft(Store.LastError);
            Db = QuestStore.Load();
            Store.MigrateAll(Db);   // 任务库做过同名合并的，把旧 id 上的进度挪过来

            _panel = new PanelView(this);
            _stripe = new StripeView(this);
            _root = new Grid();
            _root.Children.Add(_panel);
            _root.Children.Add(_stripe);
            Content = _root;

            if (string.IsNullOrEmpty(Store.Settings.LastRegion) || Db.FindRegion(Store.Settings.LastRegion) == null)
            {
                var first = Db.Regions.Count > 0 ? Db.Regions[0] : null;
                Store.Settings.LastRegion = first == null ? "" : first.Id;
            }

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Store.Save(); };

            _fullTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _fullTimer.Tick += (s, e) => CheckFullscreen();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
            SizeChanged += (s, e) => { if (!Folded) { Store.Settings.WinW = (int)Math.Round(Width); Store.Settings.WinH = (int)Math.Round(Height); } };
            LocationChanged += OnLocationChanged;

            SetFoldedVisual(Store.Settings.Folded, false);
        }

        // ---------------- 生命周期 ----------------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.HideFromAltTab(hwnd);
            _hotkeys = new HotkeyManager(hwnd);
            ApplyHotkeys();
            _hotkeys.Toggle += (s, a) => ToggleVisible();
            _hotkeys.Lock += (s, a) => ToggleLock();
            _hotkeys.Fold += (s, a) => SetFolded(!Folded);

            var st = Store.Settings;
            if (st.WinX > -10000 && st.WinY > -10000)
            {
                Left = st.WinX; Top = st.WinY;
            }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 40;
                Top = wa.Top + 80;
            }
            // 存档里的尺寸可能被折叠那会儿写坏了，太小的直接忽略
            if (st.WinW >= 340) Width = st.WinW;
            if (st.WinH >= 360) Height = st.WinH;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _fullTimer.Start();
            // 首次启动，如果本地没有任务库，后台悄悄拉一次
            if (!QuestStore.HasLocalCopy)
                System.Threading.Tasks.Task.Run(() => TryUpdateQuiet());
            // 启动十几秒后再悄悄问一次有没有新版，别耽误开窗
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            t.Tick += (s, a) => { t.Stop(); CheckAppUpdate(manual: false); };
            t.Start();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _saveTimer.Stop();
            _fullTimer.Stop();
            if (_hotkeys != null) { _hotkeys.Dispose(); _hotkeys = null; }
            SaveGeometry();
            Store.Save();
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            if (_dragging) return;
            SaveGeometry();
        }

        private void SaveGeometry()
        {
            var st = Store.Settings;
            if (Folded) { st.StripeX = (int)Math.Round(Left); st.StripeY = (int)Math.Round(Top); }
            else
            {
                st.WinX = (int)Math.Round(Left);
                st.WinY = (int)Math.Round(Top);
                st.WinW = (int)Math.Round(Width);
                st.WinH = (int)Math.Round(Height);
            }
        }

        // ---------------- 数据访问（给控件用） ----------------

        public Region CurrentRegion
        {
            get { return Db.FindRegion(Store.Settings.LastRegion); }
        }

        /// <summary>左列当前展开的章节。一次只开一章；"-" 表示全收起。</summary>
        public string OpenChapter
        {
            get
            {
                string ch = Store.Settings.OpenChapter;
                if (ch == AllFolded) return "";
                if (!string.IsNullOrEmpty(ch) && ChapterOf(ch) != null) return ch;
                var r = CurrentRegion;
                if (r != null) return string.IsNullOrEmpty(r.Chapter) ? "其他" : r.Chapter;
                return "";
            }
        }

        /// <summary>章节全收起时存进设置里的标记（空串留给「还没设过」）。</summary>
        private const string AllFolded = "-";

        /// <summary>点章节头：已经开着的收起来，别的就换成它。</summary>
        public void ToggleChapter(string chapter)
        {
            string prev = OpenChapter;

            if (string.Equals(prev, chapter, StringComparison.Ordinal))
            {
                Store.Settings.OpenChapter = AllFolded;
                SaveSoon();
                Invalidate();
                return;
            }

            Store.Settings.OpenChapter = chapter;

            // 展开的这一章跟当前选中的区域不是同一章时，把选中挪到那章第一个
            var r = CurrentRegion;
            string cur = r == null ? "" : (string.IsNullOrEmpty(r.Chapter) ? "其他" : r.Chapter);
            if (!string.Equals(cur, chapter, StringComparison.Ordinal))
            {
                var first = ChapterOf(chapter);
                if (first != null) { Store.Settings.LastRegion = first.Id; SelectedQuest = null; }
            }
            SaveSoon();
            Invalidate();
        }

        private void Invalidate()
        {
            _panel.InvalidateVisual();
            _stripe.InvalidateVisual();
        }

        private Region ChapterOf(string chapter)
        {
            foreach (var r in Db.Regions)
            {
                string c = string.IsNullOrEmpty(r.Chapter) ? "其他" : r.Chapter;
                if (string.Equals(c, chapter, StringComparison.Ordinal)) return r;
            }
            return null;
        }

        public int StateOf(Quest q)
        {
            return Store.Active.Get(q.Id);
        }

        public void CountRegion(Region r, out int done, out int total)
        {
            done = 0; total = 0;
            if (r == null) return;
            foreach (var q in r.Quests)
            {
                total++;
                if (StateOf(q) == QuestState.Done) done++;
            }
        }

        // ---------------- 交互 ----------------

        public void SelectRegion(string id)
        {
            Store.Settings.LastRegion = id;
            // 切到别的章节时，把那一章展开——否则点进去的位置会被收起来
            var r = Db.FindRegion(id);
            if (r != null)
            {
                string ch = string.IsNullOrEmpty(r.Chapter) ? "其他" : r.Chapter;
                if (!string.Equals(Store.Settings.OpenChapter, ch, StringComparison.Ordinal))
                    Store.Settings.OpenChapter = ch;
            }
            SelectedQuest = null;
            SaveSoon();
        }

        public void CycleState(Quest q)
        {
            var st = Store.Active.GetOrCreate(q.Id);
            int next = (st.S + 1) % 3;
            Store.Active.Set(q.Id, next);
            SaveSoon();
        }

        public void BeginDrag()
        {
            if (Locked) return;
            _dragging = true;
            _dragOrigin = new Point(Left, Top);
            _dragStart = new Rect(Left, Top, Width, Height);

            // 全程用物理像素的光标位置算位移，最后再按 DPI 缩放除回去换成 DIP。
            // 之前用 PointToScreen 拿的是物理像素，直接加到 Left/Top（DIP）上，
            // 150% 缩放下窗口跑得比鼠标快一半，看着就是「越拖越错位」。
            var scale = CompositionScale();
            Win32.POINT p;
            Win32.GetCursorPos(out p);
            _dragAnchor = new Point(p.X, p.Y);
            _dragAnchorScale = scale;

            CaptureMouse();
            MouseMove += OnDragMove;
            MouseLeftButtonUp += OnDragEnd;
        }

        /// <summary>当前窗口的物理像素 / DIP 比例（150% 缩放时是 1.5）。</summary>
        private double CompositionScale()
        {
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return 1.0;
            var m = src.CompositionTarget.TransformToDevice;
            double s = m.M11;
            return s > 0.1 ? s : 1.0;
        }

        private Point _dragAnchor;
        private double _dragAnchorScale = 1.0;

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Win32.POINT p;
            if (!Win32.GetCursorPos(out p)) return;
            double s = _dragAnchorScale > 0.1 ? _dragAnchorScale : 1.0;
            Left = Math.Round(_dragStart.X + (p.X - _dragAnchor.X) / s);
            Top = Math.Round(_dragStart.Y + (p.Y - _dragAnchor.Y) / s);
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            MouseMove -= OnDragMove;
            MouseLeftButtonUp -= OnDragEnd;
            SaveGeometry();
            SaveSoon();
        }

        public void SaveSoon()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void ToggleVisible()
        {
            if (IsVisible && Visibility == Visibility.Visible)
            {
                SaveGeometry();
                Store.Save();
                Hide();
            }
            else
            {
                Show();
                RestoreGeometry();
                Activate();
            }
        }

        public void HidePanel()
        {
            SaveGeometry();
            Store.Save();
            Hide();
        }

        private void RestoreGeometry()
        {
            var st = Store.Settings;
            if (Folded)
            {
                Width = 236; Height = 30;
                if (st.StripeX > -10000) { Left = st.StripeX; Top = st.StripeY; }
            }
            else
            {
                if (st.WinW > 200) Width = st.WinW;
                if (st.WinH > 200) Height = st.WinH;
                if (st.WinX > -10000) { Left = st.WinX; Top = st.WinY; }
            }
        }

        public void ToggleLock()
        {
            Locked = !Locked;
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetClickThrough(hwnd, Locked);
            if (Locked) _panel.InvalidateVisual();
            else _panel.InvalidateVisual();
        }

        public void SetFolded(bool folded)
        {
            if (Folded == folded) return;
            SaveGeometry();
            SetFoldedVisual(folded, true);
            Store.Settings.Folded = folded;
            SaveSoon();
        }

        internal void SetFoldedVisual(bool folded, bool animate)
        {
            Folded = folded;
            if (folded)
            {
                // 先把下限放掉，否则 300 的 Min 会把 236×30 夹回去
                MinWidth = 0; MinHeight = 0;
                Width = 236;
                Height = 30;
                _stripe.Visibility = Visibility.Visible;
                _panel.Visibility = Visibility.Collapsed;
                MinWidth = 236; MinHeight = 30;
            }
            else
            {
                MinWidth = 0; MinHeight = 0;
                _stripe.Visibility = Visibility.Collapsed;
                _panel.Visibility = Visibility.Visible;
                // 存档里的尺寸可能被折叠那会儿写坏了（比如只有 320 高），
                // 太小就回到默认，不至于展开成一条缝
                int ww = Store.Settings.WinW >= 340 ? Store.Settings.WinW : 392;
                int wh = Store.Settings.WinH >= 360 ? Store.Settings.WinH : 440;
                Width = ww;
                Height = wh;
                MinWidth = 300; MinHeight = 360;
            }
            _stripe.InvalidateVisual();
            _panel.InvalidateVisual();
        }

        // ---------------- 备注编辑 ----------------

        public void BeginNoteEdit(Quest q, Rect where)
        {
            EndNoteEdit(false);
            SelectedQuest = q;

            var s = Store.Active.GetOrCreate(q.Id);
            _noteBox = new TextBox
            {
                Text = s.Note ?? "",
                FontFamily = Theme.Family,
                FontSize = 11,
                Foreground = Theme.BrTxt,
                Background = new SolidColorBrush(Color.FromArgb(250, 28, 31, 34)),
                BorderBrush = Theme.BrAccDim,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                CaretBrush = Theme.BrAcc,
                SelectionBrush = Theme.BrAccDim,
                Height = 18,
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false,
                MaxLength = 80
            };
            _noteBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter) { EndNoteEdit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndNoteEdit(false); e.Handled = true; }
            };
            _noteBox.LostFocus += (sender, e) => EndNoteEdit(true);

            var geo = _panel.GetQuestNoteRect(q);
            _noteBox.Width = Math.Max(60, geo.Width);
            Canvas.SetLeft(_noteBox, geo.X);
            Canvas.SetTop(_noteBox, geo.Y);

            _overlay = _overlay ?? BuildOverlay();
            _overlay.Children.Add(_noteBox);
            _noteBox.Focus();
            _noteBox.SelectAll();
            _panel.InvalidateVisual();
        }

        private Canvas _overlay;

        private Canvas BuildOverlay()
        {
            _overlay = new Canvas { IsHitTestVisible = true, Background = null };
            _root.Children.Add(_overlay);
            return _overlay;
        }

        private void EndNoteEdit(bool commit)
        {
            if (_noteBox == null) return;
            var box = _noteBox;
            _noteBox = null;
            if (_overlay != null) _overlay.Children.Remove(box);

            if (commit && SelectedQuest != null)
            {
                var s = Store.Active.GetOrCreate(SelectedQuest.Id);
                s.Note = box.Text.Trim();
                Store.Active.Set(SelectedQuest.Id, s.S);
                SaveSoon();
            }
            _panel.InvalidateVisual();
        }

        // ---------------- 菜单 ----------------

        public void ShowQuestMenu(Quest q, UIElement anchor, Point at)
        {
            var m = new DarkMenu();
            m.AddHeader(q.Name);
            m.AddItem("标记为 未开始", () => { Store.Active.Set(q.Id, QuestState.None); SaveSoon(); _panel.InvalidateVisual(); }, StateOf(q) == QuestState.None);
            m.AddItem("标记为 进行中", () => { Store.Active.Set(q.Id, QuestState.Doing); SaveSoon(); _panel.InvalidateVisual(); }, StateOf(q) == QuestState.Doing);
            m.AddItem("标记为 已完成", () => { Store.Active.Set(q.Id, QuestState.Done); SaveSoon(); _panel.InvalidateVisual(); }, StateOf(q) == QuestState.Done);
            m.AddSeparator();
            m.AddItem("写备注…", () => BeginNoteEdit(q, Rect.Empty));
            if (!string.IsNullOrEmpty(q.Url))
                m.AddItem("在浏览器里打开攻略", () => OpenUrl(q.Url));
            m.ShowAt(anchor);
        }

        public void ShowProfileMenu()
        {
            var m = new DarkMenu();
            m.AddHeader("存档");
            foreach (var name in new List<string>(Store.Profiles.Keys))
            {
                string n = name;
                m.AddCheck(n, n == Store.ActiveName, () =>
                {
                    Store.ActiveName = n;
                    SaveSoon();
                    _panel.InvalidateVisual();
                    _stripe.InvalidateVisual();
                });
            }
            m.AddSeparator();
            m.AddItem("新建存档…", () => AddProfile());
            m.AddItem("重命名当前存档…", () => RenameProfile());
            m.AddItem("删除当前存档…", () => DeleteProfile());
            m.ShowAt(_panel);
        }

        public void ShowMainMenu()
        {
            var m = new DarkMenu();
            m.AddHeader("显示");

            string pct = Math.Round(Store.Settings.Opacity * 100) + "%";
            m.AddSlider("透明度", 0.30, 1.00, Store.Settings.Opacity, v =>
            {
                Store.Settings.Opacity = v;
                _panel.InvalidateVisual();
                _stripe.InvalidateVisual();
            }, () => SaveSoon());

            m.AddSeparator();
            m.AddCheck("只看未完成", Store.Settings.FilterIncomplete, () =>
            {
                Store.Settings.FilterIncomplete = !Store.Settings.FilterIncomplete;
                SaveSoon();
                _panel.InvalidateVisual();
            });
            m.AddCheck(Db.Regions.Count > 0 && string.IsNullOrEmpty(Db.Regions[0].Chapter)
                    ? "隐藏做完的版本" : "隐藏做完的区域",
                Store.Settings.HideDoneRegions, () =>
            {
                Store.Settings.HideDoneRegions = !Store.Settings.HideDoneRegions;
                SaveSoon();
                _panel.InvalidateVisual();
                _stripe.InvalidateVisual();
            });
            m.AddItem(Locked ? "解锁（允许点击）" : "锁定（点击穿透）", ToggleLock);
            m.AddItem(Folded ? "展开完整面板" : "折叠成细条", () => SetFolded(!Folded));

            m.AddSeparator();
            m.AddHeader("数据");
            m.AddItem("检查任务库更新…", () => CheckUpdate(manual: true));
            m.AddItem("任务列表来源…", ShowSourceInfo);
            m.AddItem(Locked ? "快捷键…（解锁后可改）" : "快捷键…", () => ShowHotkeyDialog(), Locked);

            m.AddSeparator();
            m.AddHeader("关于");
            m.AddItem("检查程序更新…", () => CheckAppUpdate(manual: true));
            m.AddItem("版本 " + App.Version + " · 查看更新日志", () => OpenUrl(QuestStore.RepoUrl + "/releases"));
            m.AddItem("项目主页", () => OpenUrl(QuestStore.RepoUrl));
            m.AddItem("退出", () => Close());

            m.ShowAt(_panel);
        }

        private void ShowSourceInfo()
        {
            var m = new DarkMenu();
            m.AddHeader("任务库");
            m.AddItem("区域 " + Db.Regions.Count + " 个 · 任务 " + CountAllQuests() + " 条", null, true);
            m.AddItem(Db.GameVersion.Length > 0 ? "数据版本 " + Db.GameVersion : "数据版本 未知", null, true);
            m.AddItem(QuestStore.SourceNote, null, true);
            int dup = Db.CountMerged();
            if (dup > 0) m.AddItem("同名已合并 " + dup + " 条", null, true);
            m.AddSeparator();
            m.AddItem("任务归属是怎么定出来的…", () => OpenUrl(QuestStore.RepoUrl + "/blob/main/docs/任务库来源.md"));
            m.AddItem("重新读取本地任务库", () =>
            {
                Db = QuestStore.Load();
                Store.MigrateAll(Db);
                _panel.InvalidateVisual();
                _stripe.InvalidateVisual();
            });
            m.ShowAt(_panel);
        }

        private int CountAllQuests()
        {
            int n = 0;
            foreach (var r in Db.Regions) n += r.Quests.Count;
            return n;
        }

        private void ShowHotkeyDialog()
        {
            var w = new HotkeyWindow(this);
            w.Owner = this;
            w.ShowDialog();
        }

        public void ApplyHotkeys()
        {
            if (_hotkeys == null) return;
            var st = Store.Settings;
            var errs = new List<string>();
            string e1 = _hotkeys.Register(HotkeyManager.IdToggle, st.HkToggle);
            string e2 = _hotkeys.Register(HotkeyManager.IdLock, st.HkLock);
            string e3 = _hotkeys.Register(HotkeyManager.IdFold, st.HkFold);
            if (e1 != null) errs.Add("显隐 " + st.HkToggle + "：" + e1);
            if (e2 != null) errs.Add("锁定 " + st.HkLock + "：" + e2);
            if (e3 != null) errs.Add("折叠 " + st.HkFold + "：" + e3);
            if (errs.Count > 0)
                ShowNotice(string.Join("；", errs));
        }

        // ---------------- 轻提示 ----------------

        public string NoticeText { get; private set; }
        private DispatcherTimer _noticeTimer;

        /// <summary>不打断操作的一句提示，画在面板底部，几秒后自己散掉。</summary>
        public void ShowNotice(string text, double seconds = 4.0)
        {
            NoticeText = text;
            if (_noticeTimer == null)
            {
                _noticeTimer = new DispatcherTimer();
                _noticeTimer.Tick += (s, e) =>
                {
                    _noticeTimer.Stop();
                    NoticeText = null;
                    _panel.InvalidateVisual();
                };
            }
            _noticeTimer.Stop();
            _noticeTimer.Interval = TimeSpan.FromSeconds(seconds);
            _noticeTimer.Start();
            _panel.InvalidateVisual();
        }

        // ---------------- 存档 ----------------

        private void AddProfile()
        {
            var name = InputBox("新存档叫什么名字？", "新建存档", "新存档");
            if (string.IsNullOrEmpty(name)) return;
            if (!Store.CreateProfile(name))
            {
                MessageBox.Show(this, "这个名字已经有了。", "建不了", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Store.ActiveName = name;
            SaveSoon();
            _panel.InvalidateVisual();
        }

        private void RenameProfile()
        {
            var name = InputBox("改成什么名字？", "重命名存档", Store.ActiveName);
            if (string.IsNullOrEmpty(name) || name == Store.ActiveName) return;
            if (!Store.RenameProfile(Store.ActiveName, name))
            {
                MessageBox.Show(this, "这个名字已经有了。", "改不了", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _panel.InvalidateVisual();
        }

        private void DeleteProfile()
        {
            if (Store.Profiles.Count <= 1)
            {
                MessageBox.Show(this, "最后一个存档删不了。", "不行", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this, "删掉存档「" + Store.ActiveName + "」？\n里面的勾选和备注会一起没。", "确认删除",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            Store.DeleteProfile(Store.ActiveName);
            var e = Store.Profiles.Keys.GetEnumerator();
            if (e.MoveNext()) Store.ActiveName = e.Current;
            SaveSoon();
            _panel.InvalidateVisual();
        }

        private string InputBox(string prompt, string title, string def)
        {
            var w = new InputWindow(prompt, title, def);
            w.Owner = this;
            return w.ShowDialog() == true ? w.Value : null;
        }

        // ---------------- 更新 ----------------

        private bool _checking;

        public void CheckUpdate(bool manual)
        {
            if (_checking) return;
            _checking = true;
            if (manual) SetBusy(true);

            System.Threading.Tasks.Task.Run(() =>
            {
                string msg;
                QuestDatabase fetched;
                var cur = Db;
                bool ok = QuestStore.TryFetchRemote(cur, out msg, out fetched);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _checking = false;
                    if (manual) SetBusy(false);
                    if (!ok)
                    {
                        if (manual) MessageBox.Show(this, msg, "拉取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Db = fetched;
                    int moved = Store.MigrateAll(Db);
                    _panel.InvalidateVisual();
                    _stripe.InvalidateVisual();
                    if (manual)
                        MessageBox.Show(this, msg + (moved > 0 ? "\n有 " + moved + " 条旧进度跟着合并挪过来了。" : ""),
                            "更新完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        private void TryUpdateQuiet()
        {
            string msg;
            QuestDatabase fetched;
            var cur = Db;
            if (!QuestStore.TryFetchRemote(cur, out msg, out fetched)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Db = fetched;
                Store.MigrateAll(Db);
                _panel.InvalidateVisual();
                _stripe.InvalidateVisual();
            }));
        }

        // ---------------- 程序自身的更新 ----------------

        private bool _checkingApp;

        public void CheckAppUpdate(bool manual)
        {
            if (_checkingApp) return;
            _checkingApp = true;
            if (manual) SetBusy(true);

            System.Threading.Tasks.Task.Run(() =>
            {
                string err;
                AppUpdateInfo info = AppUpdate.Check(out err);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _checkingApp = false;
                    if (manual) SetBusy(false);

                    if (info == null)
                    {
                        if (manual) ShowNotice("查更新失败：" + (err ?? "网络不通"), 6);
                        return;
                    }

                    bool newer = AppUpdate.IsNewer(info.Version, App.Version);
                    if (!newer)
                    {
                        if (manual || info.Version != App.Version)
                            ShowNotice("已经是最新的（" + App.Version + "）", 5);
                        return;
                    }

                    ShowAppUpdateMenu(info);
                }));
            });
        }

        private void ShowAppUpdateMenu(AppUpdateInfo info)
        {
            var m = new DarkMenu();
            m.AddHeader("有新版本");
            m.AddItem("现在是 " + App.Version + " → " + info.Version, null, true);
            m.AddSeparator();
            if (!string.IsNullOrEmpty(info.AssetUrl))
                m.AddItem("下载新版压缩包", () => OpenUrl(info.AssetUrl));
            m.AddItem("打开发布页", () => OpenUrl(string.IsNullOrEmpty(info.HtmlUrl)
                ? QuestStore.RepoUrl + "/releases" : info.HtmlUrl));
            m.AddSeparator();
            m.AddHeader("怎么换");
            m.AddItem("关掉旧 exe，解压覆盖", null, true);
            m.AddItem("存档不会丢", null, true);
            m.ShowAt(_panel);
        }

        private void SetBusy(bool busy)
        {
            Title = busy ? "鸣潮 · 查漏补缺（正在拉取…）" : "鸣潮 · 查漏补缺";
        }

        // ---------------- 全屏检测 ----------------

        private void CheckFullscreen()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            bool fs = Win32.IsForegroundFullscreen(hwnd);
            if (fs != FullscreenHint)
            {
                FullscreenHint = fs;
                _panel.InvalidateVisual();
            }
        }

        // ---------------- 杂项 ----------------

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Locked) return;

            // 窗口上的本地快捷键，只在窗口有焦点时生效
            if (e.Key == Key.Escape)
            {
                if (_noteBox != null) { EndNoteEdit(false); e.Handled = true; }
                return;
            }
            if (e.Key == Key.Space && SelectedQuest != null && _noteBox == null)
            {
                CycleState(SelectedQuest);
                _panel.InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.System || e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) return;

            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                var r = CurrentRegion;
                if (r == null) return;
                int idx = Db.Regions.IndexOf(r);
                if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
                {
                    // 下一条任务
                    var list = CurrentShown();
                    if (list.Count > 0)
                    {
                        int i = SelectedQuest == null ? -1 : list.IndexOf(SelectedQuest);
                        i = Math.Min(list.Count - 1, i + 1);
                        SelectedQuest = list[i];
                        ScrollToSelected();
                        _panel.InvalidateVisual();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Up)
                {
                    var list = CurrentShown();
                    if (list.Count > 0)
                    {
                        int i = SelectedQuest == null ? list.Count : list.IndexOf(SelectedQuest);
                        i = Math.Max(0, i - 1);
                        SelectedQuest = list[i];
                        ScrollToSelected();
                        _panel.InvalidateVisual();
                        e.Handled = true;
                    }
                }
            }
        }

        public List<Quest> CurrentShown()
        {
            var r = CurrentRegion;
            var list = new List<Quest>();
            if (r == null) return list;

            string tf = Store.Settings.TypeFilter;
            bool byType = !string.IsNullOrEmpty(tf);
            if (byType)
            {
                bool any = false;
                foreach (var q in r.Quests) if (string.Equals(q.Type, tf, StringComparison.Ordinal)) { any = true; break; }
                // 这个区域里没有这一类的任务（比如切到只有潮汐的地区），就别把人筛空了
                if (!any) byType = false;
            }

            foreach (var q in r.Quests)
            {
                if (byType && !string.Equals(q.Type, tf, StringComparison.Ordinal)) continue;
                if (Store.Settings.FilterIncomplete && StateOf(q) == QuestState.Done) continue;
                list.Add(q);
            }
            return list;
        }

        private void ScrollToSelected() { }
    }
}
