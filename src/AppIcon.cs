using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WuwaChecklist
{
    /// <summary>
    /// 窗口图标。ico 已经作为 Resource 编进 exe（同一个文件也喂给了
    /// ApplicationIcon，所以资源管理器里看到的和窗口标题栏是同一张）。
    /// 读失败就算了，不要为了一个图标把程序拦下来。
    /// </summary>
    internal static class AppIcon
    {
        private static ImageSource _cached;

        public static ImageSource Load()
        {
            if (_cached != null) return _cached;
            try
            {
                var res = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
                if (res == null) return null;
                using (var s = res.Stream)
                {
                    // 选 256 那帧当源，WPF 自己按需缩，小尺寸也清楚
                    var dec = new IconBitmapDecoder(s,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    BitmapFrame best = dec.Frames.Count > 0 ? dec.Frames[0] : null;
                    foreach (var f in dec.Frames)
                        if (best == null || f.PixelWidth > best.PixelWidth) best = f;
                    if (best != null) best.Freeze();
                    _cached = best;
                }
            }
            catch (Exception)
            {
                _cached = null;
            }
            return _cached;
        }
    }
}
