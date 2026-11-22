using System;
using System.Windows;
using System.Windows.Threading;

namespace WuwaChecklist
{
    internal sealed class App : Application
    {
        public const string Version = "0.4.0";

        private MainWindow _mw;
        private System.Threading.Mutex _mutex;

        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
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
