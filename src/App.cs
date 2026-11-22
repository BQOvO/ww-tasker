using System;
using System.Windows;
using System.Windows.Threading;

namespace WuwaChecklist
{
    internal sealed class App : Application
    {
        public const string Version = "0.6.0";

        private MainWindow _mw;
        private System.Threading.Mutex _mutex;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--shot")
            {
                RunShot(args);
                return;
            }
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        /// <summary>
        /// 离屏渲染界面截图，给 README 用。
        /// 用法：WuwaChecklist.exe --shot 输出.png [宽 高] [--folded]
        /// 走这条路是因为面板半透明，截屏会把桌面一起拍进去。
        /// </summary>
        private static void RunShot(string[] args)
        {
            string outPath = null;
            double w = 392, h = 440;
            bool folded = false;
            bool backdrop = true;
            bool hasW = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--folded") { folded = true; continue; }
                if (a == "--plain") { backdrop = false; continue; }
                if (a == "--shot") { if (i + 1 < args.Length) outPath = args[++i]; continue; }
                double v;
                if (double.TryParse(a, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    if (!hasW) { w = v; hasW = true; } else h = v;
                }
            }
            if (string.IsNullOrEmpty(outPath)) return;

            // WPF 控件要一个 Application 实例，这里不需要消息循环
            var app = new App();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                Shot.Render(outPath, w, h, folded, backdrop);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
            Environment.Exit(0);
        }

        private void InitializeComponent()
        {
            bool created;
            _mutex = new System.Threading.Mutex(true, @"Global\ww-tasker-single", out created);
            if (!created)
            {
                MessageBox.Show("已经开着一个了。\n找找托盘右边、屏幕角落里那条细条。", "鸣潮 · 查漏补缺",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += OnCrash;

            _mw = new MainWindow();
            MainWindow = _mw;
            _mw.Show();
        }

        /// <summary>不打断操作的提示：画在面板上，几秒后自己消失。</summary>
        public static void ReportSoft(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var app = Current as App;
            if (app != null && app._mw != null) app._mw.ShowNotice(message);
        }

        private void OnCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "出了个错，程序没死，但这一下没做完。\n\n" + e.Exception.Message +
                "\n\n" + (e.Exception.StackTrace ?? ""),
                "出错", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
