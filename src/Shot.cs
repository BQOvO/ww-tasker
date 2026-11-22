using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WuwaChecklist
{
    /// <summary>
    /// 把界面离屏渲染成 PNG，给 README 用。
    ///
    /// 为什么不用截屏：面板是半透明的，截屏一定把后面的桌面、任务栏、别的窗口一起拍进去，
    /// 那玩意儿不能进 README。这里直接问 WPF 要它自己画出来的东西——面板之外一个像素都没有。
    /// 底下那层渐变是代码画的假游戏画面，专门用来看清半透明的效果。
    /// </summary>
    internal static class Shot
    {
        public static void Render(string path, double w, double h, bool folded, bool withBackdrop)
        {
            var win = new MainWindow();
            // 截图固定按 75% 走，别被本机存档里的透明度带跑
            win.Store.Settings.Opacity = 0.75;

            if (folded) win.SetFoldedVisual(folded: true, animate: false);
            else
            {
                win.Width = w;
                win.Height = h;
            }

            var root = win.Content as FrameworkElement;
            if (root == null) throw new InvalidOperationException("窗口没有 Content");

            double rw = folded ? win.Width : w;
            double rh = folded ? win.Height : h;

            root.Measure(new Size(rw, rh));
            root.Arrange(new Rect(0, 0, rw, rh));
            root.UpdateLayout();

            // 面板和底色必须在同一张 DrawingVisual 里一次画完。
            // 分两次 RenderTargetBitmap.Render 是不行的——第二次会把前一次的内容盖掉。
            var stage = new DrawingVisual();
            using (var dc = stage.RenderOpen())
            {
                if (withBackdrop) PaintBackdrop(dc, rw, rh);
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null,
                    new Rect(0, 0, rw, rh));
            }

            // 2 倍超采样，缩到 1 倍时文字边缘才不糊
            const double ss = 2.0;
            var rtb = new RenderTargetBitmap(
                (int)Math.Round(rw * ss), (int)Math.Round(rh * ss),
                96.0 * ss, 96.0 * ss, PixelFormats.Pbgra32);
            rtb.Render(stage);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>
        /// 假的「游戏画面」：上暗下亮的青灰渐变 + 两团柔光。
        /// 亮度得压着面板走——面板底只透出 25%，底色太亮会显得面板像块塑料板。
        /// </summary>
        private static void PaintBackdrop(DrawingContext dc, double w, double h)
        {
            var brush = new LinearGradientBrush(
                Color.FromRgb(0x4C, 0x5F, 0x66),
                Color.FromRgb(0xA9, 0xB8, 0xAE),
                new Point(0, 0), new Point(0.4, 1));
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));

            var glow = new RadialGradientBrush(
                Color.FromArgb(0xB0, 0xEC, 0xF0, 0xDC),
                Color.FromArgb(0x00, 0xEC, 0xF0, 0xDC));
            dc.DrawEllipse(glow, null, new Point(w * 0.86, h * 0.16), w * 0.5, h * 0.4);

            var glow2 = new RadialGradientBrush(
                Color.FromArgb(0x80, 0x62, 0x8A, 0x94),
                Color.FromArgb(0x00, 0x62, 0x8A, 0x94));
            dc.DrawEllipse(glow2, null, new Point(w * 0.10, h * 0.92), w * 0.62, h * 0.42);
        }
    }
}
