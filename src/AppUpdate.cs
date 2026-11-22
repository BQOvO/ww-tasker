using System;
using System.Net;
using System.Text;

namespace WuwaChecklist
{
    /// <summary>
    /// 程序自身的更新检查。跟任务库更新分开：
    /// 任务库改数据不用重装，程序更新得换 exe。
    /// </summary>
    internal sealed class AppUpdateInfo
    {
        public string Version;     // 去掉 v 前缀的版本号，比如 0.2.0
        public string Tag;         // 原始 tag
        public string Name;        // release 标题
        public string Notes;       // release 正文
        public string HtmlUrl;     // 发布页
        public string AssetUrl;    // zip 直链，可能为空
        public string AssetName;
    }

    internal static class AppUpdate
    {
        public const string ApiUrl = "https://api.github.com/repos/BQOvO/ww-tasker/releases/latest";

        /// <summary>
        /// 查最新 release。返回 null 表示查询失败。
        /// 全部同步执行，调用方丢线程里。
        /// </summary>
        public static AppUpdateInfo Check(out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                string text;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "ww-tasker");
                    wc.Headers.Add("Accept", "application/vnd.github+json");
                    text = wc.DownloadString(ApiUrl);
                }

                object parsed;
                string perr;
                if (!Json.TryParse(text, out parsed, out perr))
                {
                    error = "返回内容解析不过（" + perr + "）。";
                    return null;
                }

                var root = Json.Obj(parsed);
                if (root == null)
                {
                    error = "返回内容不是对象。";
                    return null;
                }

                var info = new AppUpdateInfo
                {
                    Tag = Json.Str(root, "tag_name"),
                    Name = Json.Str(root, "name"),
                    Notes = Json.Str(root, "body"),
                    HtmlUrl = Json.Str(root, "html_url")
                };
                info.Version = TrimTag(info.Tag);

                var assets = Json.Arr(root.ContainsKey("assets") ? root["assets"] : null);
                if (assets != null)
                {
                    foreach (var a in assets)
                    {
                        var d = Json.Obj(a);
                        if (d == null) continue;
                        string n = Json.Str(d, "name");
                        if (string.IsNullOrEmpty(n)) continue;
                        if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            info.AssetName = n;
                            info.AssetUrl = Json.Str(d, "browser_download_url");
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(info.Version))
                {
                    error = "没有版本号。";
                    return null;
                }
                return info;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>v0.2.0 → 0.2.0；顺手去掉空白和多余前缀。</summary>
        public static string TrimTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string s = tag.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);
            return s.Trim();
        }

        /// <summary>latest 比 current 新吗。解析不出来就当作不比。</summary>
        public static bool IsNewer(string latest, string current)
        {
            int[] a, b;
            if (!ParseVersion(latest, out a)) return false;
            if (!ParseVersion(current, out b)) return false;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x > y) return true;
                if (x < y) return false;
            }
            return false;
        }

        private static bool ParseVersion(string v, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(v)) return false;
            var list = new System.Collections.Generic.List<int>();
            var sb = new StringBuilder();
            foreach (char c in v)
            {
                if (c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length == 0) continue;
                    list.Add(int.Parse(sb.ToString()));
                    sb.Length = 0;
                    if (c == '-' || c == '+') break;
                }
            }
            if (sb.Length > 0) list.Add(int.Parse(sb.ToString()));
            if (list.Count == 0) return false;
            parts = list.ToArray();
            return true;
        }
    }
}
