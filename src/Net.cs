using System;
using System.IO;
using System.Net;
using System.Text;

namespace WuwaChecklist
{
    /// <summary>
    /// 就两个 GET 的地方，抽出来是因为 WebClient.DownloadString 的默认超时是 100 秒——
    /// 网一卡，后台线程吊在那儿，前台的「正在拉取…」要挂一分多钟，第二次点还没反应。
    /// </summary>
    internal static class Net
    {
        /// <summary>同步 GET 一段文本。超时丢 WebException，调用方自己接。</summary>
        public static string Get(string url, int timeoutMs, string accept = null, bool noCache = false)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ww-tasker";
            req.Accept = string.IsNullOrEmpty(accept) ? "*/*" : accept;
            if (noCache)
            {
                req.Headers.Add("Cache-Control", "no-cache");
                req.Headers.Add("Pragma", "no-cache");
            }
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>把 WebException 翻成人话——用户看不懂「基础连接已经关闭」。</summary>
        public static string Explain(Exception ex)
        {
            var we = ex as WebException;
            if (we != null)
            {
                if (we.Status == WebExceptionStatus.Timeout)
                    return "超时了，网不太通。";
                if (we.Status == WebExceptionStatus.NameResolutionFailure ||
                    we.Status == WebExceptionStatus.ConnectFailure ||
                    we.Status == WebExceptionStatus.ProxyNameResolutionFailure)
                    return "连不上，估计是没网或者被墙了。";

                var hr = we.Response as HttpWebResponse;
                if (hr != null)
                    return "服务器回了 " + (int)hr.StatusCode + " " + hr.StatusDescription + "。";
            }
            if (ex is IOException)
                return "读到一半断了。";
            return ex.Message;
        }
    }
}
